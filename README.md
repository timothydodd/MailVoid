

# MailVoid
MailVoid is a simple yet powerful tool designed for developers who need to manage multiple test emails without the hassle of creating multiple email accounts.

## Features
API: Built with C# .NET 8, the API contains endpoints for the web frontend and an endpoint that receives webhook events from SendGrid.
Frontend: A simple web view of the different mailboxes received from a particular domain, coded in Angular 18.
Authentication: Uses Auth0 for authentication, which can be easily swapped for your own preferred method.
Database: Requires a MySQL database to store emails.

## Requirements
.NET 8
MySQL
Angular 18
SendGrid account
Auth0 account (optional, for authentication)

## Setup

Backend
- Clone the repository:
``` bash
git clone https://github.com/timothydodd/MailVoid.git
```

- Navigate to the API project directory:
``` bash
cd src/MailVoidApi
```

- Update the appsettings.json file with your MySQL and auth0 config.
```
dotnet run
```

## Frontend
- Navigate to the Web project directory:
```
cd MailVoid/src/MailVoidWeb
```
- Update the environment.ts file with your Auth0 configuration (if using).
- Install Angular dependencies:
  ```
  npm install
  ```
- Run the Angular project:
  ```
  npm start
  ```
  
## Usage
1. Access the frontend at http://localhost:6200.
2. Log in using Auth0 (or your preferred authentication method).
3. View and manage the emails received from your specified domain.
 
##Contributing
Contributions are welcome! Please open an issue or submit a pull request.

License
This project is licensed under the MIT License.

Acknowledgments
SendGrid
Auth0
