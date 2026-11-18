-- Row-level security: the second layer of authorization (AGENTS.md). Applied as ehr_owner after migrations, on every
-- run of infra/migrate; safe to re-run. A missed check in the app must still return no unauthorized rows, so RLS is
-- enabled AND forced on every PHI table and on the audit log; forced, so even the owner is bound by the policies.
--
-- Who the current user is comes from app.user_id, which UserContextInterceptor (Ehr.Data) sets with
-- set_config(..., true) as the first statement of every transaction. With nobody signed in it is '' rather than
-- NULL, which is why current_user_id() goes through NULLIF. The functions run as the caller: the account tables are
-- not under RLS, and both runtime roles may read them.
--
-- Every policy calls them as scalar subqueries, (SELECT ehr.is_clinician()) rather than ehr.is_clinician(), so that
-- PostgreSQL runs each once per statement, as an InitPlan. Called directly, they ran once for every row the statement
-- looked at, since PostgreSQL can't inline a function whose body is a subquery: two lookups in the account tables per
-- row, 200 ms for a search across 50,000 patients. It is the same answer either way: the functions are STABLE, take no
-- arguments, and read only app.user_id and the account tables, never the row, so within a statement every call returns
-- the same value. Ehr.Security.Tests fails if a policy calls one directly.
--
-- The access model: a clinician sees and writes every patient in the clinic, and keeps a panel of their own
-- patients (patient_assignments), which is what they see first; a patient sees their own record; an admin manages
-- accounts and reads the audit log, but has no access to patient data. Opening the chart of a patient off one's
-- panel asks for a reason, which the app records (Ehr.Domain.Patients.BreakGlass); the database does not gate it.

CREATE OR REPLACE FUNCTION ehr.current_user_id() RETURNS uuid
    LANGUAGE sql STABLE
    RETURN NULLIF(current_setting('app.user_id', true), '')::uuid;

-- The roles of the signed-in staff account, or NULL when the user is not an active staff account. A locked-out
-- account (how an account is disabled) loses its access here too, not only at sign-in.
CREATE OR REPLACE FUNCTION ehr.current_staff_roles() RETURNS text[]
    LANGUAGE sql STABLE
    RETURN (
        SELECT roles FROM ehr.staff_users
        WHERE id = ehr.current_user_id() AND (lockout_end IS NULL OR lockout_end <= now())
    );

CREATE OR REPLACE FUNCTION ehr.is_clinician() RETURNS boolean
    LANGUAGE sql STABLE
    RETURN coalesce(ehr.current_staff_roles() @> ARRAY['Clinician'], false);

CREATE OR REPLACE FUNCTION ehr.is_admin() RETURNS boolean
    LANGUAGE sql STABLE
    RETURN coalesce(ehr.current_staff_roles() @> ARRAY['Admin'], false);

-- The patient the signed-in patient account belongs to, or NULL when the user is not an active patient account.
CREATE OR REPLACE FUNCTION ehr.current_patient_id() RETURNS uuid
    LANGUAGE sql STABLE
    RETURN (
        SELECT patient_id FROM ehr.patient_users
        WHERE id = ehr.current_user_id() AND (lockout_end IS NULL OR lockout_end <= now())
    );

-- Patients (PHI). Clinicians read and write; a patient reads their own row. Nobody deletes (10-grants.sql).
ALTER TABLE ehr.patients ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.patients FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS patients_clinician_select ON ehr.patients;
CREATE POLICY patients_clinician_select ON ehr.patients FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS patients_clinician_insert ON ehr.patients;
CREATE POLICY patients_clinician_insert ON ehr.patients FOR INSERT WITH CHECK ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS patients_clinician_update ON ehr.patients;
CREATE POLICY patients_clinician_update ON ehr.patients FOR UPDATE
    USING ((SELECT ehr.is_clinician())) WITH CHECK ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS patients_own_select ON ehr.patients;
CREATE POLICY patients_own_select ON ehr.patients FOR SELECT USING (id = (SELECT ehr.current_patient_id()));

-- Patient assignments (PHI). Clinicians read every panel; a clinician puts patients on their own panel and ends their
-- own assignments, nobody else's; a patient reads who their clinicians are. Nobody deletes (10-grants.sql).
ALTER TABLE ehr.patient_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.patient_assignments FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS patient_assignments_clinician_select ON ehr.patient_assignments;
CREATE POLICY patient_assignments_clinician_select ON ehr.patient_assignments FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS patient_assignments_own_insert ON ehr.patient_assignments;
CREATE POLICY patient_assignments_own_insert ON ehr.patient_assignments FOR INSERT
    WITH CHECK ((SELECT ehr.is_clinician()) AND clinician_id = (SELECT ehr.current_user_id()));

DROP POLICY IF EXISTS patient_assignments_own_update ON ehr.patient_assignments;
CREATE POLICY patient_assignments_own_update ON ehr.patient_assignments FOR UPDATE
    USING ((SELECT ehr.is_clinician()) AND clinician_id = (SELECT ehr.current_user_id()))
    WITH CHECK ((SELECT ehr.is_clinician()) AND clinician_id = (SELECT ehr.current_user_id()));

DROP POLICY IF EXISTS patient_assignments_patient_select ON ehr.patient_assignments;
CREATE POLICY patient_assignments_patient_select ON ehr.patient_assignments FOR SELECT
    USING (patient_id = (SELECT ehr.current_patient_id()));

-- Emergency access (PHI). A clinician records and reads their own; admins review it in the audit log, where the
-- break_glass event carries the same reason. Nothing updates or deletes (10-grants.sql).
ALTER TABLE ehr.break_glass_accesses ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.break_glass_accesses FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS break_glass_accesses_own_select ON ehr.break_glass_accesses;
CREATE POLICY break_glass_accesses_own_select ON ehr.break_glass_accesses FOR SELECT
    USING ((SELECT ehr.is_clinician()) AND clinician_id = (SELECT ehr.current_user_id()));

DROP POLICY IF EXISTS break_glass_accesses_own_insert ON ehr.break_glass_accesses;
CREATE POLICY break_glass_accesses_own_insert ON ehr.break_glass_accesses FOR INSERT
    WITH CHECK ((SELECT ehr.is_clinician()) AND clinician_id = (SELECT ehr.current_user_id()));

-- The rest of the chart (PHI): allergies, medications, diagnoses and visits, each row one patient's. Clinicians read
-- every patient's; a patient reads their own. There are no write policies: nothing at runtime writes these tables yet
-- (10-grants.sql), and with row-level security forced a write that got past the grants would still be refused.
ALTER TABLE ehr.allergy_intolerances ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.allergy_intolerances FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS allergy_intolerances_clinician_select ON ehr.allergy_intolerances;
CREATE POLICY allergy_intolerances_clinician_select ON ehr.allergy_intolerances FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS allergy_intolerances_own_select ON ehr.allergy_intolerances;
CREATE POLICY allergy_intolerances_own_select ON ehr.allergy_intolerances FOR SELECT
    USING (patient_id = (SELECT ehr.current_patient_id()));

ALTER TABLE ehr.medication_statements ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.medication_statements FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS medication_statements_clinician_select ON ehr.medication_statements;
CREATE POLICY medication_statements_clinician_select ON ehr.medication_statements FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS medication_statements_own_select ON ehr.medication_statements;
CREATE POLICY medication_statements_own_select ON ehr.medication_statements FOR SELECT
    USING (patient_id = (SELECT ehr.current_patient_id()));

ALTER TABLE ehr.conditions ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.conditions FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS conditions_clinician_select ON ehr.conditions;
CREATE POLICY conditions_clinician_select ON ehr.conditions FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS conditions_own_select ON ehr.conditions;
CREATE POLICY conditions_own_select ON ehr.conditions FOR SELECT
    USING (patient_id = (SELECT ehr.current_patient_id()));

ALTER TABLE ehr.encounters ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.encounters FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS encounters_clinician_select ON ehr.encounters;
CREATE POLICY encounters_clinician_select ON ehr.encounters FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS encounters_own_select ON ehr.encounters;
CREATE POLICY encounters_own_select ON ehr.encounters FOR SELECT
    USING (patient_id = (SELECT ehr.current_patient_id()));

-- Test results (PHI). Clinicians read every patient's. A patient reads only their own, and only once the result is
-- released and finished (Observation.IssuedAt, ObservationStatus.Released): a result still being worked on, cancelled
-- or entered in error never reaches the portal, even through a bug in the app. No write policies, as for the chart.
ALTER TABLE ehr.observations ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.observations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS observations_clinician_select ON ehr.observations;
CREATE POLICY observations_clinician_select ON ehr.observations FOR SELECT USING ((SELECT ehr.is_clinician()));

DROP POLICY IF EXISTS observations_own_released_select ON ehr.observations;
CREATE POLICY observations_own_released_select ON ehr.observations FOR SELECT
    USING (patient_id = (SELECT ehr.current_patient_id())
        AND issued_at IS NOT NULL
        AND status IN ('final', 'amended', 'corrected'));

-- Audit events (restricted). Admins read. Anyone the grants let insert may insert: the app role for read and
-- sign-in events, and the write-audit trigger (40-audit.sql), which runs as the table's owner and is bound by this
-- policy too because RLS is forced. Nothing updates or deletes (10-grants.sql).
ALTER TABLE ehr.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.audit_events FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS audit_events_admin_select ON ehr.audit_events;
CREATE POLICY audit_events_admin_select ON ehr.audit_events FOR SELECT USING ((SELECT ehr.is_admin()));

DROP POLICY IF EXISTS audit_events_insert ON ehr.audit_events;
CREATE POLICY audit_events_insert ON ehr.audit_events FOR INSERT WITH CHECK (true);

-- Drafts are private to their author and require an active panel assignment.
ALTER TABLE ehr.clinical_drafts ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.clinical_drafts FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS clinical_drafts_own ON ehr.clinical_drafts;
CREATE POLICY clinical_drafts_own ON ehr.clinical_drafts FOR ALL
    USING (
        (SELECT ehr.is_clinician())
        AND clinician_id = (SELECT ehr.current_user_id())
        AND EXISTS (
            SELECT 1 FROM ehr.patient_assignments a
            WHERE a.clinician_id = (SELECT ehr.current_user_id())
              AND a.patient_id = clinical_drafts.patient_id AND a.ended_at IS NULL
        )
    );

-- Signed notes follow the chart's clinician access model. Drafts remain private under UC 8's panel policy.
ALTER TABLE ehr.clinical_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.clinical_notes FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS clinical_notes_clinician_select ON ehr.clinical_notes;
CREATE POLICY clinical_notes_clinician_select ON ehr.clinical_notes FOR SELECT
    USING ((SELECT ehr.is_clinician()));
DROP POLICY IF EXISTS clinical_notes_own_insert ON ehr.clinical_notes;
CREATE POLICY clinical_notes_own_insert ON ehr.clinical_notes FOR INSERT
    WITH CHECK ((SELECT ehr.is_clinician()) AND signer_id = (SELECT ehr.current_user_id()));

-- UC 1 (Tahseen Arnan). Orders (PHI) are read as signed notes are: by every clinician, on the chart's access model. A
-- clinician signs an order only as themselves and only for a patient on their active panel, the rule for writing notes;
-- the signing trigger also locks, checks for a duplicate and sets the time (60-orders.sql). The one update is
-- discontinuing an order, by the same rule: a clinician, for a patient on their active panel, recorded as themselves
-- (the trigger sets that before this check). Nothing deletes an order (10-grants.sql), so there is no policy for it.
ALTER TABLE ehr.orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.orders FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS orders_clinician_select ON ehr.orders;
CREATE POLICY orders_clinician_select ON ehr.orders FOR SELECT
    USING ((SELECT ehr.is_clinician()));
DROP POLICY IF EXISTS orders_own_insert ON ehr.orders;
CREATE POLICY orders_own_insert ON ehr.orders FOR INSERT
    WITH CHECK (
        (SELECT ehr.is_clinician())
        AND signer_id = (SELECT ehr.current_user_id())
        AND EXISTS (
            SELECT 1 FROM ehr.patient_assignments a
            WHERE a.clinician_id = (SELECT ehr.current_user_id())
              AND a.patient_id = orders.patient_id AND a.ended_at IS NULL
        )
    );
DROP POLICY IF EXISTS orders_panel_update ON ehr.orders;
CREATE POLICY orders_panel_update ON ehr.orders FOR UPDATE
    USING (
        (SELECT ehr.is_clinician())
        AND EXISTS (
            SELECT 1 FROM ehr.patient_assignments a
            WHERE a.clinician_id = (SELECT ehr.current_user_id())
              AND a.patient_id = orders.patient_id AND a.ended_at IS NULL
        )
    )
    WITH CHECK (
        (SELECT ehr.is_clinician())
        AND discontinuer_id = (SELECT ehr.current_user_id())
        AND EXISTS (
            SELECT 1 FROM ehr.patient_assignments a
            WHERE a.clinician_id = (SELECT ehr.current_user_id())
              AND a.patient_id = orders.patient_id AND a.ended_at IS NULL
        )
    );

-- UC 13 review results are private to the clinician and the same active-panel draft as UC 14.
ALTER TABLE ehr.ai_note_reviews ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.ai_note_reviews FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS ai_note_reviews_own ON ehr.ai_note_reviews;
CREATE POLICY ai_note_reviews_own ON ehr.ai_note_reviews FOR ALL
    USING ((SELECT ehr.is_clinician()) AND clinician_id = (SELECT ehr.current_user_id())
        AND EXISTS (SELECT 1 FROM ehr.clinical_drafts d
            WHERE d.id = ai_note_reviews.draft_id AND d.patient_id = ai_note_reviews.patient_id
              AND d.clinician_id = (SELECT ehr.current_user_id())));

ALTER TABLE ehr.ai_field_proposals ENABLE ROW LEVEL SECURITY;
ALTER TABLE ehr.ai_field_proposals FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS ai_field_proposals_own ON ehr.ai_field_proposals;
CREATE POLICY ai_field_proposals_own ON ehr.ai_field_proposals FOR ALL
    USING ((SELECT ehr.is_clinician()) AND EXISTS (SELECT 1 FROM ehr.ai_note_reviews r
        WHERE r.id = ai_field_proposals.review_id AND r.patient_id = ai_field_proposals.patient_id));
