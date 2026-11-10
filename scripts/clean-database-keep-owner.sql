-- Empties the LOCAL development database of everything except the owner account roy@sanddata.no.
--
--   set -a; . ./.env; set +a
--   docker exec -i -e PGPASSWORD="$POSTGRES_PASSWORD" nosanddataamsapi-db-1 \
--     psql -U amsadmin -d amsdb -v ON_ERROR_STOP=1 < scripts/clean-database-keep-owner.sql
--
-- Run it after the migrations are applied (starting the API applies them).
-- NEVER run this against production. It deletes every location, meter, measurement, sensor key and other user.
--
-- Kept: the owner user with its roles and refresh tokens, the Role table (seeded by a migration) and
--       __EFMigrationsHistory (so the migrations are not applied again).
-- Kept on purpose: ElectricityPrice and ExchangeRate. They are fetched data, not user data, and are refilled
--       slowly by the background jobs. Uncomment the two DELETEs at the end to clear them too.
-- Everything runs in one transaction: it stops with nothing changed if the owner is missing.

BEGIN;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "User" WHERE "Email" = 'roy@sanddata.no') THEN
        RAISE EXCEPTION 'The user roy@sanddata.no does not exist; nothing was deleted';
    END IF;
END
$$;

-- Readings and meters, then the links, locations and their sensor keys.
DELETE FROM "Measurement";
DELETE FROM "Meter";
DELETE FROM "UserLocation";
-- A location owns its sensor key: deleting the locations deletes their keys too.
DELETE FROM "Location";

-- Every other user; their roles, refresh tokens and links go with them (ON DELETE CASCADE).
DELETE FROM "User" WHERE "Email" <> 'roy@sanddata.no';

-- DELETE FROM "ElectricityPrice";
-- DELETE FROM "ExchangeRate";

-- What is left:
SELECT 'User' AS "table", count(*) FROM "User"
UNION ALL SELECT 'UserRole', count(*) FROM "UserRole"
UNION ALL SELECT 'Role', count(*) FROM "Role"
UNION ALL SELECT 'RefreshTokens', count(*) FROM "RefreshTokens"
UNION ALL SELECT 'Location', count(*) FROM "Location"
UNION ALL SELECT 'ApiKey', count(*) FROM "ApiKey"
UNION ALL SELECT 'Meter', count(*) FROM "Meter"
UNION ALL SELECT 'UserLocation', count(*) FROM "UserLocation"
UNION ALL SELECT 'Measurement', count(*) FROM "Measurement"
UNION ALL SELECT 'ElectricityPrice', count(*) FROM "ElectricityPrice"
UNION ALL SELECT 'ExchangeRate', count(*) FROM "ExchangeRate"
ORDER BY 1;

COMMIT;
