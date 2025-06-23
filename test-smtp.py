#!/usr/bin/env python3
import smtplib
import sys
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart

def test_smtp_connection(server_host, server_port=25):
    print(f"Testing SMTP connection to {server_host}:{server_port}")
    
    try:
        # Create SMTP connection
        print("1. Attempting to connect...")
        server = smtplib.SMTP(server_host, server_port, timeout=10)
        server.set_debuglevel(2)  # Enable debug output
        
        print("2. Connected! Sending HELO...")
        server.helo('test-client')
        
        print("3. Connection successful!")
        
        # Try to send a test email
        try:
            print("4. Attempting to send test email...")
            msg = MIMEMultipart()
            msg['From'] = 'test@example.com'
            msg['To'] = 'test@voidcrew.dbmk2.com'
            msg['Subject'] = 'Test Email from SMTP Client'
            
            body = "This is a test email to verify SMTP server functionality."
            msg.attach(MIMEText(body, 'plain'))
            
            server.sendmail('test@example.com', ['test@voidcrew.dbmk2.com'], msg.as_string())
            print("5. Test email sent successfully!")
        except Exception as e:
            print(f"4. Could not send email: {e}")
        
        server.quit()
        return True
        
    except smtplib.SMTPServerDisconnected:
        print("ERROR: Server disconnected unexpectedly")
        return False
    except smtplib.SMTPConnectError as e:
        print(f"ERROR: Failed to connect to SMTP server: {e}")
        return False
    except Exception as e:
        print(f"ERROR: {type(e).__name__}: {e}")
        return False

if __name__ == "__main__":
    # Test the SMTP server
    host = "voidcrew.dbmk2.com"
    port = 25
    
    if len(sys.argv) > 1:
        host = sys.argv[1]
    if len(sys.argv) > 2:
        port = int(sys.argv[2])
    
    success = test_smtp_connection(host, port)
    
    if success:
        print("\n✓ SMTP server is accessible and responding")
    else:
        print("\n✗ SMTP server is not accessible")
        print("\nPossible issues:")
        print("- Port 25 is blocked by firewall or ISP/cloud provider")
        print("- SMTP service is not running on the server")
        print("- DNS is not resolving correctly")