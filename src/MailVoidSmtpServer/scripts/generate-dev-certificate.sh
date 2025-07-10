#!/bin/bash
# Bash script to generate a self-signed certificate for development
# This creates a certificate suitable for testing SMTP over TLS

CERT_PATH="../certs"
PASSWORD="development"
DNS_NAME="localhost"
DAYS=1825  # 5 years

# Create certs directory if it doesn't exist
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
CERT_DIR="$SCRIPT_DIR/$CERT_PATH"

if [ ! -d "$CERT_DIR" ]; then
    mkdir -p "$CERT_DIR"
    echo "Created certificate directory: $CERT_DIR"
fi

# Generate private key
openssl genrsa -out "$CERT_DIR/smtp-dev.key" 2048

# Generate certificate signing request
openssl req -new -key "$CERT_DIR/smtp-dev.key" -out "$CERT_DIR/smtp-dev.csr" -subj "/C=US/ST=State/L=City/O=MailVoid Development/CN=$DNS_NAME"

# Create extensions file for SAN (Subject Alternative Names)
cat > "$CERT_DIR/smtp-dev.ext" << EOF
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
DNS.2 = *.mailvoid.local
DNS.3 = smtp.mailvoid.local
IP.1 = 127.0.0.1
IP.2 = ::1
EOF

# Generate self-signed certificate
openssl x509 -req -in "$CERT_DIR/smtp-dev.csr" -signkey "$CERT_DIR/smtp-dev.key" -out "$CERT_DIR/smtp-dev.crt" -days $DAYS -sha256 -extfile "$CERT_DIR/smtp-dev.ext"

# Create PFX/PKCS12 file
openssl pkcs12 -export -out "$CERT_DIR/smtp-dev.pfx" -inkey "$CERT_DIR/smtp-dev.key" -in "$CERT_DIR/smtp-dev.crt" -password pass:$PASSWORD

# Clean up temporary files
rm -f "$CERT_DIR/smtp-dev.csr" "$CERT_DIR/smtp-dev.ext"

echo ""
echo "Development certificate generated successfully!"
echo "Certificate files created:"
echo "  - PFX format: $CERT_DIR/smtp-dev.pfx"
echo "  - PEM format: $CERT_DIR/smtp-dev.crt and smtp-dev.key"
echo "  - Password: $PASSWORD"
echo ""
echo "To use this certificate, update your appsettings.Development.json:"
echo '{'
echo '  "SmtpServer": {'
echo '    "EnableSsl": true,'
echo '    "CertificatePath": "./certs/smtp-dev.pfx",'
echo '    "CertificatePassword": "'$PASSWORD'"'
echo '  }'
echo '}'

# Make script executable
chmod +x "$SCRIPT_DIR/generate-dev-certificate.sh"