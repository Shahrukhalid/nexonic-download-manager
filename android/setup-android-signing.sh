#!/usr/bin/env bash
# Creates the Android release signing key on this Mac and stores it as GitHub repository secrets,
# so the Android workflow signs the release APK. The key never goes into the repository.
# KEEP the folder it creates: every future update of the app must be signed with this same key.
set -euo pipefail
REPO="Shahrukhalid/nexonic-download-manager"
DIR="$HOME/Vision Quest AI/nexonic-android-signing"
KS="$DIR/nexonic-release.p12"
ALIAS="nexonic"

command -v gh >/dev/null || { echo "gh is not installed (brew install gh, then gh auth login)."; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "gh is not signed in: run gh auth login first."; exit 1; }

mkdir -p "$DIR"; chmod 700 "$DIR"
if [ -f "$KS" ]; then
  echo "Using the existing key in $DIR"
  PASS="$(cat "$DIR/password.txt")"
else
  PASS="$(openssl rand -base64 32 | tr -dc 'A-Za-z0-9' | head -c 32)"
  KEY="$(mktemp)"; CERT="$(mktemp)"
  openssl req -x509 -newkey rsa:4096 -sha256 -days 10950 -nodes -keyout "$KEY" -out "$CERT" \
    -subj "/CN=Shahrukh Khalid/O=Nexonic/C=PK" 2>/dev/null
  # PKCS12 with widely supported algorithms so every JDK can read it.
  openssl pkcs12 -export -name "$ALIAS" -inkey "$KEY" -in "$CERT" -out "$KS" -passout "pass:$PASS" \
    -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 2>/dev/null \
    || openssl pkcs12 -export -name "$ALIAS" -inkey "$KEY" -in "$CERT" -out "$KS" -passout "pass:$PASS"
  rm -f "$KEY" "$CERT"
  printf '%s' "$PASS" > "$DIR/password.txt"
  chmod 600 "$KS" "$DIR/password.txt"
  cat > "$DIR/README.txt" <<TXT
Nexonic Download Manager — Android release signing key (created $(date +%F)).
Back up this whole folder somewhere safe (e.g. an encrypted USB stick or password manager).
If it is lost, existing installs can't be updated: users would have to uninstall and reinstall.
Never commit these files. Key alias: $ALIAS. The store and key password is in password.txt.
TXT
  echo "Created the signing key in $DIR"
fi

base64 < "$KS" | tr -d '\n' | gh secret set NDM_ANDROID_KEYSTORE_B64 -R "$REPO"
printf '%s' "$PASS" | gh secret set NDM_ANDROID_KEYSTORE_PASSWORD -R "$REPO"
printf '%s' "$ALIAS" | gh secret set NDM_ANDROID_KEY_ALIAS -R "$REPO"
printf '%s' "$PASS" | gh secret set NDM_ANDROID_KEY_PASSWORD -R "$REPO"
echo "DONE. The 4 secrets are set on $REPO. Back up: $DIR"
