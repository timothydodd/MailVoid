# MailVoid
MailVoid is a simple yet powerful tool designed for developers who need to manage multiple test emails without the hassle of creating multiple email accounts. This project is built using .NET 8 and consists of two main components: an API that handles webhook events from SendGrid's email parser API, and a Razor Web API that displays all emails sent to your specified domain. This can be extremely useful for testing and development purposes.

## Features
Webhook Listener: Captures and processes emails sent via SendGrid's email parser API, making it easy to handle incoming test emails dynamically.
Email Display: A Razor Web interface that lists all emails received by the domain, providing a consolidated view of your test emails.
## Prerequisites
Before setting up MailVoid, you will need:

- .NET 8
- MySQL Database

## Getting Started
Step 1: Clone the Repository
``` bash
git clone https://github.com/yourgithubusername/MailVoid.git
cd MailVoid
```

# Step 2: Set Up the Database
Create a MySQL database named MailVoidDB and import the initial schema (found in the db directory of this repository).

# Step 3: Configure Your Environment
Copy the .env.example file to .env and update it with your database connection details and other configurations.

# Step 4: Run the Applications
Navigate to the API and Web projects and run them using the following commands:

``` bash
cd src\MailVoidApi
dotnet run

cd src\MailVoidWeb
dotnet run
```

## Usage
Once both applications are running:

The API will listen for incoming webhook events at http://localhost:8080/api/mail.
You can view the received emails at http://localhost:8081.

## Contributing
Contributions are welcome! Please fork the repository and submit pull requests to the main branch.

## License
Distributed under the MIT License. See LICENSE for more information.

## Acknowledgements
SendGrid for their email parser API
.NET community for continuous support
