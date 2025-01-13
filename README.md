

# MailVoid
MailVoid is a simple yet powerful tool designed for developers who need to manage multiple test emails without the hassle of creating multiple email accounts.

> [!WARNING]  
 This project is currently in prototype status. Please be aware that things might change often

## Features
- API: Built with C# .NET 8, the API contains endpoints for the web frontend and an endpoint that receives webhook events from SendGrid.
- Frontend: A simple web view of the different mailboxes received from a particular domain, coded in Angular 18.
- Authentication: Uses JWT Auth 
- Database: Requires a MySQL database to store emails.

## Requirements
.NET 8
MySQL
Angular 18
SendGrid account

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

- Update the appsettings.json file with your MySQL and JwtSettings config the Secret must be a SymmetricSecurityKey.
```
dotnet run 
```
This will also run the front end.


## Frontend
- Navigate to the Web project directory:
```
cd MailVoid/src/MailVoidWeb
```
- Update the environment.ts file with your Own  configuration (if using).
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
2. Log in using default credentials user/pass is admin
3. View and manage the emails received from your specified domain.
 
##Contributing
This project is in prototype phase so contributions will be welcome, but I would wait until project is closer to 1.0.

License
This project is licensed under the MIT License.

Acknowledgments
SendGrid
Microsoft 
