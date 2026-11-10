#!/usr/bin/env bash
set -euo pipefail
umask 077
certificate_dir="${1:-.keys/vercel}"
if [[ -e "$certificate_dir" ]]; then
  echo "Refusing to overwrite $certificate_dir. Keep the existing certificate for future deployments." >&2
  exit 1
fi
command -v openssl >/dev/null || { echo 'Install OpenSSL before running this script.' >&2; exit 1; }
mkdir -p "$certificate_dir"
chmod 700 "$certificate_dir"
openssl rand -base64 32 > "$certificate_dir/password.txt"
openssl req -x509 -newkey rsa:3072 -sha256 -noenc -days 3650 \
  -subj '/CN=Pillar Data Protection/' \
  -keyout "$certificate_dir/private-key.pem" -out "$certificate_dir/certificate.pem" 2>/dev/null
openssl pkcs12 -export -inkey "$certificate_dir/private-key.pem" -in "$certificate_dir/certificate.pem" \
  -out "$certificate_dir/certificate.pfx" -passout "file:$certificate_dir/password.txt"
openssl base64 -A -in "$certificate_dir/certificate.pfx" -out "$certificate_dir/certificate-base64.txt"
chmod 600 "$certificate_dir/"*
# The PFX contains the private key; redundant plaintext PEM material is unnecessary.
rm "$certificate_dir/private-key.pem" "$certificate_dir/certificate.pem"
printf 'Created certificate files in %s (no secrets printed).\n' "$certificate_dir"
printf 'Security__DataProtectionCertificate: contents of certificate-base64.txt\n'
printf 'Security__DataProtectionCertificatePassword: contents of password.txt\n'
printf 'Keep a private backup of these files. Reuse them for every production deployment.\n'
