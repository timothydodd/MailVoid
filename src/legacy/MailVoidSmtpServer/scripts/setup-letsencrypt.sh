#!/bin/bash
# Setup Let's Encrypt certificate for SMTP server with automatic renewal

DOMAIN="smtp.yourdomain.com"
CERT_PASSWORD="your-secure-password"
SMTP_SERVICE_NAME="mailvoid-smtp"

echo "Setting up Let's Encrypt certificate for $DOMAIN"

# Install certbot if not already installed
if ! command -v certbot &> /dev/null; then
    echo "Installing certbot..."
    sudo apt update
    sudo apt install -y certbot
fi

# Generate certificate using standalone mode
echo "Generating certificate for $DOMAIN..."
sudo certbot certonly --standalone --agree-tos --email admin@$DOMAIN -d $DOMAIN

# Convert to PFX format
echo "Converting certificate to PFX format..."
sudo openssl pkcs12 -export -out /etc/letsencrypt/live/$DOMAIN/smtp.pfx \
    -inkey /etc/letsencrypt/live/$DOMAIN/privkey.pem \
    -in /etc/letsencrypt/live/$DOMAIN/fullchain.pem \
    -password pass:$CERT_PASSWORD

# Set proper permissions
sudo chmod 644 /etc/letsencrypt/live/$DOMAIN/smtp.pfx
sudo chown root:root /etc/letsencrypt/live/$DOMAIN/smtp.pfx

# Create renewal hook script
sudo tee /etc/letsencrypt/renewal-hooks/deploy/smtp-renewal.sh > /dev/null << EOF
#!/bin/bash
# Renewal hook for SMTP server

DOMAIN="$DOMAIN"
CERT_PASSWORD="$CERT_PASSWORD"

# Convert renewed certificate to PFX
openssl pkcs12 -export -out /etc/letsencrypt/live/\$DOMAIN/smtp.pfx \
    -inkey /etc/letsencrypt/live/\$DOMAIN/privkey.pem \
    -in /etc/letsencrypt/live/\$DOMAIN/fullchain.pem \
    -password pass:\$CERT_PASSWORD

# Set permissions
chmod 644 /etc/letsencrypt/live/\$DOMAIN/smtp.pfx
chown root:root /etc/letsencrypt/live/\$DOMAIN/smtp.pfx

# Restart SMTP service
systemctl restart $SMTP_SERVICE_NAME || true
EOF

# Make renewal hook executable
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/smtp-renewal.sh

# Test automatic renewal
echo "Testing automatic renewal..."
sudo certbot renew --dry-run

# Add cron job for automatic renewal (if not already present)
if ! sudo crontab -l | grep -q "certbot renew"; then
    echo "Adding cron job for automatic renewal..."
    (sudo crontab -l 2>/dev/null; echo "0 12 * * * /usr/bin/certbot renew --quiet") | sudo crontab -
fi

echo ""
echo "Let's Encrypt setup complete!"
echo "Certificate path: /etc/letsencrypt/live/$DOMAIN/smtp.pfx"
echo "Certificate password: $CERT_PASSWORD"
echo ""
echo "Update your appsettings.json with:"
echo '{'
echo '  "SmtpServer": {'
echo '    "EnableSsl": true,'
echo '    "CertificatePath": "/etc/letsencrypt/live/'$DOMAIN'/smtp.pfx",'
echo '    "CertificatePassword": "'$CERT_PASSWORD'"'
echo '  }'
echo '}'
echo ""
echo "Automatic renewal is configured and will run daily at 12:00 PM"