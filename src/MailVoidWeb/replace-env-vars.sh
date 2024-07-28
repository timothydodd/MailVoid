#!/bin/sh

# Replace placeholders in the environment.ts file

find '/usr/share/nginx/html' -name '*.js' -exec sed -i -e 's,${API_URL},'"$API_URL"',g' {} \;
# Start Nginx
exec "$@"