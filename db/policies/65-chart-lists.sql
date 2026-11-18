-- Editable allergies, medications and diagnoses (UC 4; the expansion plan's step 2). Applied as ehr_owner after
-- migrations, on every run of infra/migrate; safe to re-run. The grants are in 10-grants.sql and the row-level security
-- policies in 30-rls.sql: a clinician adds and changes entries only for a patient on their active panel, and nothing
-- deletes one.

-- Whether an ICD-10-CM code is in a sensitive range. The rule, recorded in docs/expansion/PLAN.md's decision log:
--   F01-F99           mental, behavioral and neurodevelopmental disorders, substance use (F10-F19) included
--   A50-A64           infections with a predominantly sexual mode of transmission
--   B20, Z21          HIV disease, and asymptomatic HIV infection status
--   O04, Z33.2        termination of pregnancy
--   T74, T76          abuse, neglect and other maltreatment, confirmed or suspected
--   X71-X83, R45.851  intentional self-harm, and suicidal ideations
-- Codes are compared without regard to case, with or without the dot. Not a table: the rule changes by review here.
CREATE OR REPLACE FUNCTION ehr.icd10cm_is_sensitive(code text) RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    PARALLEL SAFE
    SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(
        c ~ '^F[0-9][0-9A-Z]'
        OR (c ~ '^A[0-9]{2}' AND substr(c, 1, 3) BETWEEN 'A50' AND 'A64')
        OR c ~ '^(B20|Z21|O04|Z332|T74|T76|R45851)'
        OR (c ~ '^X[0-9]{2}' AND substr(c, 1, 3) BETWEEN 'X71' AND 'X83'),
        false)
    FROM (SELECT upper(replace(code, '.', '')) AS c) AS normalized
$$;
REVOKE ALL ON FUNCTION ehr.icd10cm_is_sensitive(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION ehr.icd10cm_is_sensitive(text) TO ehr_app, ehr_read;

-- Before each insert or update of a chart entry. Runs as the caller, so the grants and row-level security bind it.
--   1. An entry never moves: not to another patient, and not to another ID.
--   2. A diagnosis whose code is in a sensitive range is sensitive, whatever the client sent: the app can't clear it.
CREATE OR REPLACE FUNCTION ehr.guard_chart_entry() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND (NEW.id IS DISTINCT FROM OLD.id OR NEW.patient_id IS DISTINCT FROM OLD.patient_id) THEN
        RAISE EXCEPTION 'A chart entry stays with its patient' USING ERRCODE = '42501';
    END IF;

    IF TG_TABLE_NAME = 'conditions' AND ehr.icd10cm_is_sensitive(NEW.code) THEN
        NEW.sensitive := true;
    END IF;

    RETURN NEW;
END
$$;
REVOKE ALL ON FUNCTION ehr.guard_chart_entry() FROM PUBLIC;

CREATE OR REPLACE TRIGGER allergy_intolerances_guard
    BEFORE INSERT OR UPDATE ON ehr.allergy_intolerances
    FOR EACH ROW EXECUTE FUNCTION ehr.guard_chart_entry();
CREATE OR REPLACE TRIGGER medication_statements_guard
    BEFORE INSERT OR UPDATE ON ehr.medication_statements
    FOR EACH ROW EXECUTE FUNCTION ehr.guard_chart_entry();
CREATE OR REPLACE TRIGGER conditions_guard
    BEFORE INSERT OR UPDATE ON ehr.conditions
    FOR EACH ROW EXECUTE FUNCTION ehr.guard_chart_entry();

-- The history clinicians read: after each insert or update of a chart entry, who changed it, when, and the entry
-- before and after. It runs as its owner, ehr_owner (SECURITY DEFINER), because nothing at runtime may write
-- ehr.chart_history: so the history can't be skipped, forged or edited. Its search_path is pinned, and nobody but its
-- owner may call it directly. An update that changes nothing adds nothing. The write audit (40-audit.sql) records the
-- same changes for admins; this is the copy the care team sees beside the chart.
CREATE OR REPLACE FUNCTION ehr.record_chart_history() RETURNS trigger
    LANGUAGE plpgsql
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND to_jsonb(NEW) = to_jsonb(OLD) THEN
        RETURN NULL;
    END IF;

    INSERT INTO ehr.chart_history (id, patient_id, table_name, row_id, changed_at, changed_by_id, before, after)
    VALUES (
        uuidv7(),
        NEW.patient_id,
        TG_TABLE_NAME,
        NEW.id,
        clock_timestamp(),
        NULLIF(current_setting('app.user_id', true), '')::uuid,
        CASE WHEN TG_OP = 'UPDATE' THEN to_jsonb(OLD) END,
        to_jsonb(NEW));
    RETURN NULL;
END
$$;
REVOKE ALL ON FUNCTION ehr.record_chart_history() FROM PUBLIC;

CREATE OR REPLACE TRIGGER allergy_intolerances_history
    AFTER INSERT OR UPDATE ON ehr.allergy_intolerances
    FOR EACH ROW EXECUTE FUNCTION ehr.record_chart_history();
CREATE OR REPLACE TRIGGER medication_statements_history
    AFTER INSERT OR UPDATE ON ehr.medication_statements
    FOR EACH ROW EXECUTE FUNCTION ehr.record_chart_history();
CREATE OR REPLACE TRIGGER conditions_history
    AFTER INSERT OR UPDATE ON ehr.conditions
    FOR EACH ROW EXECUTE FUNCTION ehr.record_chart_history();
