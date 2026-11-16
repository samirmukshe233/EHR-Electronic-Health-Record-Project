-- UC 1 Place an order (Tahseen Arnan): signing an order. Applied as ehr_owner after migrations, on every run of
-- infra/migrate; safe to re-run. The function runs as the caller, not as its owner, so row-level security and the grants
-- bind what it reads and locks, as they bind the insert itself (30-rls.sql: a clinician, signing as themselves, for a
-- patient on their panel). There is no UPDATE or DELETE grant on orders (10-grants.sql): once signed, an order stays.
--
-- Before each insert it:
--   0. refuses any transaction that isn't READ COMMITTED, the level page handlers run at. Under REPEATABLE READ or
--      SERIALIZABLE the transaction that waited at the lock would still check for a duplicate against its old
--      snapshot, miss the order it waited for, and let both through.
--   1. locks the patient's row, FOR NO KEY UPDATE, so one patient's orders are signed one at a time: a second
--      transaction waits here until the first ends, and then sees its order. The lock needs the UPDATE on patients that
--      ehr_app has. Foreign keys to the patient take a weaker lock, which this one doesn't block, and plain reads take
--      none, so only another order (or an update of the patient) waits. Anyone but a clinician sees no patient, and is
--      refused here.
--   2. refuses an order that isn't active: nothing can change a status later, so another would be stuck as it was.
--   3. refuses an active order for an item the patient already has an active order for, unless it gives a reason. Two
--      submissions at once can't both get through: the second waited at the lock. The error is unique_violation (23505),
--      which Ehr.Domain.Orders.ClinicalOrders turns into the order form's warning.
--   4. sets the signing time to the clock's, as signing a note does, whatever the client sent: an order that waited
--      at the lock is signed after the one it waited for. The signer is the column's default, the
--      signed-in user, and the row-level security policy refuses any other.
CREATE OR REPLACE FUNCTION ehr.sign_order() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
AS $$
BEGIN
    IF current_setting('transaction_isolation') <> 'read committed' THEN
        RAISE EXCEPTION 'An order is signed in a READ COMMITTED transaction' USING ERRCODE = '0A000';
    END IF;

    PERFORM 1 FROM ehr.patients WHERE id = NEW.patient_id FOR NO KEY UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Only a clinician may sign an order for this patient' USING ERRCODE = '42501';
    END IF;

    -- Nothing can change an order's status later, and discontinuing or completing one isn't built: every order is active.
    IF NEW.status IS DISTINCT FROM 'active' THEN
        RAISE EXCEPTION 'An order is signed active' USING ERRCODE = '23514';
    END IF;

    IF coalesce(NEW.duplicate_reason, '') !~ '[^[:space:]]'
       AND EXISTS (
           SELECT 1 FROM ehr.orders o
           WHERE o.patient_id = NEW.patient_id AND o.catalog_code = NEW.catalog_code AND o.status = 'active') THEN
        RAISE EXCEPTION 'The patient already has an active order for this item; give a reason to order it again'
            USING ERRCODE = '23505';
    END IF;

    NEW.signed_at := clock_timestamp();
    RETURN NEW;
END
$$;
REVOKE ALL ON FUNCTION ehr.sign_order() FROM PUBLIC;
CREATE OR REPLACE TRIGGER orders_sign
    BEFORE INSERT ON ehr.orders
    FOR EACH ROW EXECUTE FUNCTION ehr.sign_order();
