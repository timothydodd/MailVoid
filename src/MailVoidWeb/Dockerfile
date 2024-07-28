# Stage 1: Build the Angular application
FROM node:18 AS build

# Set working directory
WORKDIR /app

# Install dependencies
COPY package*.json ./
RUN npm install

# Copy the rest of the application code
COPY . .

# Build the application
RUN npm run build --prod

# Stage 2: Serve the application using Nginx
FROM nginx:alpine

COPY replace-env-vars.sh /usr/share/nginx/html
COPY nginx.conf /usr/share/nginx/html
# Copy the built Angular application from the previous stage
COPY --from=build app/dist/mail-void/browser /usr/share/nginx/html
RUN chmod +x /usr/share/nginx/html/replace-env-vars.sh


# Expose port 80
EXPOSE 80

# Start Nginx
CMD ["/usr/share/nginx/html/replace-env-vars.sh","nginx", "-g", "daemon off;"]
