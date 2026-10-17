#!/usr/bin/env bash
set -euo pipefail
snmp_test_dir=$(mktemp -d)
snmp_test_pid=''
cleanup() {
  if [[ -n "$snmp_test_pid" ]]; then kill "$snmp_test_pid" 2>/dev/null || true; wait "$snmp_test_pid" 2>/dev/null || true; fi
  rm -rf -- "$snmp_test_dir"
}
trap cleanup EXIT
mkdir -p "$snmp_test_dir/state"
cat > "$snmp_test_dir/agent.conf" <<'CONFIG'
agentAddress udp:127.0.0.1:1161
engineID snmptool-tests
sysName snmptool-agent
sysLocation Independent interoperability test
sysContact Test only
rouser noauth noauth .1
rouser authenticated auth .1
rouser private priv .1
rouser sha384 priv .1
rouser sha512 priv .1
rouser legacysha priv .1
CONFIG
cat > "$snmp_test_dir/state/snmpd.conf" <<'USERS'
createUser noauth SHA-256 "test-authentication"
createUser authenticated SHA-256 "test-authentication"
createUser private SHA-256 "test-authentication" AES "test-privacy"
createUser sha384 SHA-384 "test-authentication" AES "test-privacy"
createUser sha512 SHA-512 "test-authentication" AES "test-privacy"
createUser legacysha SHA "test-authentication" AES "test-privacy"
USERS
export SNMP_PERSISTENT_DIR="$snmp_test_dir/state"
export SNMPCONFPATH="$snmp_test_dir/state"
export MIBS=''
snmpd -f -Lo -C -c "$snmp_test_dir/agent.conf,$snmp_test_dir/state/snmpd.conf" -p "$snmp_test_dir/agent.pid" > "$snmp_test_dir/agent.log" 2>&1 &
snmp_test_pid=$!
sleep 1
if ! kill -0 "$snmp_test_pid" 2>/dev/null; then cat "$snmp_test_dir/agent.log"; exit 1; fi
if ! dotnet run --project tests/interop/Interop.csproj --configuration Release; then cat "$snmp_test_dir/agent.log"; exit 1; fi
