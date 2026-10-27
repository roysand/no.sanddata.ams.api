#!/usr/bin/env bash
# Runs the checks in MANUAL-TEST.md (task T029) against a running API and prints PASS/FAIL per step.
#
#   ./manual-test.sh            run the test (asks for the admin password)
#   ./manual-test.sh clean      remove the test location, meter, links, key and test users from the local database
#
# Settings (environment variables, all optional):
#   API_URL         default https://localhost:7130
#   ADMIN_EMAIL     default roy@sanddata.no
#   ADMIN_PASSWORD  asked for if not set
#
# Needs: curl, jq. Use a throwaway local database: locations and links cannot be deleted through the API.

API_URL="${API_URL:-https://localhost:7130}"
ADMIN_EMAIL="${ADMIN_EMAIL:-roy@sanddata.no}"
A_EMAIL="role.a@example.com"
B_EMAIL="role.b@example.com"
USER_PASSWORD="Test-Roles1!"
SERIAL="TEST-ROLES-1"

if [[ "${1:-}" == "clean" ]]; then
    docker exec -i nosanddataamsapi-db-1 psql -U amsadmin -d amsdb <<SQL
DELETE FROM "Meter" WHERE "LocationId" IN (SELECT "Id" FROM "Location" WHERE "SerialNumber" = '$SERIAL');
DELETE FROM "UserLocation" WHERE "LocationId" IN (SELECT "Id" FROM "Location" WHERE "SerialNumber" = '$SERIAL');
WITH k AS (DELETE FROM "Location" WHERE "SerialNumber" = '$SERIAL' RETURNING "ApiKeyId")
DELETE FROM "ApiKey" WHERE "Id" IN (SELECT "ApiKeyId" FROM k);
DELETE FROM "User" WHERE "Email" IN ('$A_EMAIL', '$B_EMAIL');
SQL
    exit $?
fi

command -v jq >/dev/null || { echo "jq is required"; exit 2; }

if [[ -z "${ADMIN_PASSWORD:-}" ]]; then
    read -r -s -p "Password for $ADMIN_EMAIL: " ADMIN_PASSWORD
    echo
fi

PASS=0
FAIL=0
BODY=""
STATUS=""

# call METHOD PATH TOKEN [JSON]  ->  sets STATUS and BODY. TOKEN "-" means no token; no JSON means no body at all.
call() {
    local method="$1" path="$2" token="$3" json="${4-}"
    local args=(-sk -X "$method" -w $'\n%{http_code}' "$API_URL$path")
    [[ "$token" != "-" ]] && args+=(-H "Authorization: Bearer $token")
    [[ -n "$json" ]] && args+=(-H "Content-Type: application/json" -d "$json")
    local out
    out="$(curl "${args[@]}")"
    STATUS="${out##*$'\n'}"
    BODY="${out%$'\n'*}"
}

# check STEP DESCRIPTION EXPECTED_STATUS  -> uses STATUS and BODY from the last call
check() {
    local step="$1" what="$2" expected="$3"
    if [[ "$STATUS" == "$expected" ]]; then
        PASS=$((PASS + 1)); printf 'PASS  %-4s %s (%s)\n' "$step" "$what" "$STATUS"
    else
        FAIL=$((FAIL + 1)); printf 'FAIL  %-4s %s: expected %s, got %s\n      %s\n' "$step" "$what" "$expected" "$STATUS" "$BODY"
    fi
}

# expect STEP DESCRIPTION JQ_FILTER  -> passes when the filter is true for the last BODY
expect() {
    local step="$1" what="$2" filter="$3"
    if jq -e "$filter" <<<"$BODY" >/dev/null 2>&1; then
        PASS=$((PASS + 1)); printf 'PASS  %-4s %s\n' "$step" "$what"
    else
        FAIL=$((FAIL + 1)); printf 'FAIL  %-4s %s\n      filter: %s\n      body:   %s\n' "$step" "$what" "$filter" "$BODY"
    fi
}

login() { # EMAIL PASSWORD -> prints the access token
    call POST /api/auth/login - "$(jq -nc --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"
    jq -r '.accessToken // empty' <<<"$BODY"
}

create_user() { # FIRST EMAIL -> prints the user id (reuses the user if it already exists)
    call POST /api/users "$ADMIN" "$(jq -nc --arg e "$2" --arg f "$1" --arg p "$USER_PASSWORD" \
        '{firstName:$f,lastName:"Roles",email:$e,password:$p}')"
    if [[ "$STATUS" == "201" || "$STATUS" == "200" ]]; then
        jq -r '.id' <<<"$BODY"
    else
        call GET "/api/users?pageSize=100" "$ADMIN"
        jq -r --arg e "$2" '.users[] | select(.email == $e) | .id' <<<"$BODY"
    fi
}

echo "== Setup ($API_URL)"
ADMIN="$(login "$ADMIN_EMAIL" "$ADMIN_PASSWORD")"
[[ -n "$ADMIN" ]] || { echo "Admin login failed ($STATUS): $BODY"; exit 2; }

A_ID="$(create_user RoleA "$A_EMAIL")"
B_ID="$(create_user RoleB "$B_EMAIL")"
[[ -n "$A_ID" && -n "$B_ID" ]] || { echo "Could not create or find the test users"; exit 2; }
TOKEN_A="$(login "$A_EMAIL" "$USER_PASSWORD")"
TOKEN_B="$(login "$B_EMAIL" "$USER_PASSWORD")"
[[ -n "$TOKEN_A" && -n "$TOKEN_B" ]] || { echo "Login as A or B failed. Run './manual-test.sh clean' and try again."; exit 2; }
echo "A=$A_ID"
echo "B=$B_ID"

echo
echo "== Test"

# 1. Create the location; link A with NO body at all.
call POST /api/admin/locations "$ADMIN" "$(jq -nc --arg s "$SERIAL" '{name:"TEST-ROLES-1",address:"Test road 1",serialNumber:$s,zone:"NO1"}')"
if [[ "$STATUS" == "409" ]]; then
    echo "      (location $SERIAL already exists from an earlier run: run './manual-test.sh clean' first for a clean result)"
    call GET /api/admin/locations "$ADMIN"
    LOC="$(jq -r --arg s "$SERIAL" '[.. | objects | select(.serialNumber? == $s)][0].id' <<<"$BODY" | head -1)"
else
    check 1a "admin creates the location" 201
    LOC="$(jq -r '.location.id' <<<"$BODY")"
fi
[[ -n "$LOC" && "$LOC" != "null" ]] || { echo "No location id"; exit 2; }

call PUT "/api/users/$A_ID/locations/$LOC" "$ADMIN"
check 1b "PUT link for A with no body" 204

# 2. B as viewer
call PUT "/api/users/$B_ID/locations/$LOC" "$ADMIN" '{"role":"Viewer"}'
check 2 "B linked as Viewer" 204

# 3. Invalid roles
call PUT "/api/users/$B_ID/locations/$LOC" "$ADMIN" '{"role":"Admin"}'
check 3a 'role "Admin" is refused' 400
call PUT "/api/users/$B_ID/locations/$LOC" "$ADMIN" '{"role":"1"}'
check 3b 'role "1" is refused' 400

# 4. Last owner
call PUT "/api/users/$A_ID/locations/$LOC" "$ADMIN" '{"role":"Viewer"}'
check 4a "demoting the only owner is refused" 409
call DELETE "/api/users/$A_ID/locations/$LOC" "$ADMIN"
check 4b "unlinking the only owner is refused" 409

# 5. Admin lists
call GET /api/admin/locations "$ADMIN"
expect 5a "admin location list shows A=Owner, B=Viewer" \
    "[.. | objects | select(.serialNumber? == \"$SERIAL\")] | .[0].users | (map(select(.email==\"$A_EMAIL\"))[0].role == \"Owner\") and (map(select(.email==\"$B_EMAIL\"))[0].role == \"Viewer\")"
call GET "/api/users?pageSize=100" "$ADMIN"
expect 5b "user list has locationAccess with role, plus the old locations/locationIds" \
    "[.users[] | select(.email==\"$A_EMAIL\")][0] | (.locationAccess | map(select(.locationId==\"$LOC\"))[0].role == \"Owner\") and has(\"locations\") and has(\"locationIds\")"

# 6. Viewer lists
call GET /api/locations "$TOKEN_B"
expect 6 "B sees the location as Viewer, with serialNumber/isActive/hasNorgesPriceAgreement" \
    "[.. | objects | select(.id? == \"$LOC\")][0] | .role == \"Viewer\" and has(\"serialNumber\") and has(\"isActive\") and has(\"hasNorgesPriceAgreement\")"

# 7. Viewer cannot edit or register meters
call PUT "/api/locations/$LOC" "$TOKEN_B" '{"name":"Hacked","address":"x","isActive":true}'
check 7a "viewer cannot edit the location (404)" 404
call POST /api/meters "$TOKEN_B" "$(jq -nc --arg l "$LOC" '{locationId:$l,deviceId:"TEST:ROLES:B"}')"
check 7b "viewer cannot register a meter (404)" 404

# 8. Owner edits
call PUT "/api/locations/$LOC" "$TOKEN_A" '{"name":"New name","address":"New address","isActive":true}'
check 8a "owner edits the location" 200
call GET /api/locations "$TOKEN_A"
expect 8b "list shows the new name; serial and zone unchanged" \
    "[.. | objects | select(.id? == \"$LOC\")][0] | .name == \"New name\" and .serialNumber == \"$SERIAL\" and .zone == \"NO1\""

# 9. Extra fields ignored; isActive required
call PUT "/api/locations/$LOC" "$TOKEN_A" '{"name":"New name","address":"New address","isActive":true,"serialNumber":"HACK","zone":"NO5"}'
check 9a "extra fields accepted but ignored" 200
call GET /api/locations "$TOKEN_A"
expect 9b "serial number and zone unchanged" \
    "[.. | objects | select(.id? == \"$LOC\")][0] | .serialNumber == \"$SERIAL\" and .zone == \"NO1\""
call PUT "/api/locations/$LOC" "$TOKEN_A" '{"name":"New name","address":"New address"}'
check 9c "missing isActive is refused" 400

# 10. Deactivate
call PUT "/api/locations/$LOC" "$TOKEN_A" '{"name":"New name","address":"New address","isActive":false}'
check 10a "owner deactivates" 200
call GET /api/locations "$TOKEN_A"
expect 10b "A still sees it, inactive" "[.. | objects | select(.id? == \"$LOC\")][0] | .isActive == false"
call GET /api/locations "$TOKEN_B"
expect 10c "B no longer sees it" "[.. | objects | select(.id? == \"$LOC\")] | length == 0"

# 11. Reactivate
call PUT "/api/locations/$LOC" "$TOKEN_A" '{"name":"New name","address":"New address","isActive":true}'
check 11a "owner reactivates" 200
call GET /api/locations "$TOKEN_B"
expect 11b "B sees it again, as Viewer" "[.. | objects | select(.id? == \"$LOC\")][0] | .role == \"Viewer\""

# 12. Owner registers a meter and comments it
call POST /api/meters "$TOKEN_A" "$(jq -nc --arg l "$LOC" '{locationId:$l,deviceId:"TEST:ROLES:01"}')"
check 12a "owner registers a meter" 200
METER="$(jq -r '.id' <<<"$BODY")"
call PUT "/api/meters/$METER" "$TOKEN_A" '{"comment":"Hello"}'
check 12b "owner saves a comment" 200
expect 12c "comment saved; device id and location unchanged" \
    ".comment == \"Hello\" and .deviceId == \"TEST:ROLES:01\" and .locationId == \"$LOC\""

# 13. Viewer cannot comment
call PUT "/api/meters/$METER" "$TOKEN_B" '{"comment":"Nope"}'
check 13 "viewer cannot comment (404)" 404

# 14. Promote B, then demote A
call PUT "/api/users/$B_ID/locations/$LOC" "$ADMIN" '{"role":"Owner"}'
check 14a "B made Owner" 204
call PUT "/api/users/$A_ID/locations/$LOC" "$ADMIN" '{"role":"Viewer"}'
check 14b "A demoted to Viewer (B is now an owner)" 204

# 15. No token
call PUT "/api/locations/$LOC" - '{"name":"x","address":"x","isActive":true}'
check 15a "PUT location without token" 401
call PUT "/api/meters/$METER" - '{"comment":"x"}'
check 15b "PUT meter without token" 401
call PUT "/api/users/$B_ID/locations/$LOC" -
check 15c "PUT link without token" 401

# 16. Normal user on admin link endpoints
call PUT "/api/users/$B_ID/locations/$LOC" "$TOKEN_A"
check 16a "normal user PUT link" 403
call DELETE "/api/users/$B_ID/locations/$LOC" "$TOKEN_A"
check 16b "normal user DELETE link" 403

echo
echo "== Result: $PASS passed, $FAIL failed"
echo "Still to do by hand: the dashboard and the web app's 'Add location for user' for an existing user (see MANUAL-TEST.md)."
echo "Clean up with: ./manual-test.sh clean"
[[ "$FAIL" -eq 0 ]]
