# AcxiomCRM

AcxiomCRM is an ASP.NET Core MVC CRM targeting .NET 10. It uses ASP.NET Core Identity, Entity Framework Core, SQL Server, Bootstrap, and Chart.js.

## Requirements

- .NET 10 SDK
- SQL Server or SQL Server LocalDB
- A connection string with permission to create/alter the application database

## Configure and run

Configure values using user secrets or environment variables; do not commit production credentials:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=(localdb)\MSSQLLocalDB;Database=AcxiomCRM;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$env:BootstrapAdmin__Email = "admin@example.com"
$env:BootstrapAdmin__Password = "Use-A-Unique-Strong-Password1!"
$env:BootstrapAdmin__Name = "CRM Administrator"
dotnet run
```

The application applies EF Core migrations at startup and seeds the `Admin`, `Manager`, and `Sales Executive` roles. If both bootstrap administrator settings are provided, startup creates the initial administrator (or ensures the configured user has the Admin role). The password must satisfy the Identity password policy: at least 8 characters, including uppercase, lowercase, a digit, and a non-alphanumeric character.

To manage migrations explicitly:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
dotnet ef database update
```

`Migrations/InitialCreate.sql` is an idempotent SQL Server deployment script. For an existing database created previously with `EnsureCreated`, back up the database and baseline/transition it to migration history before deploying; migrations do not automatically baseline an `EnsureCreated` schema.

## Application structure

- `Models/` — Identity user and CRM entities
- `Data/` — EF Core context and record access scoping
- `Controllers/` — MVC, account/admin, and API endpoints
- `Services/` — audit recording
- `Views/` — responsive MVC screens
- `Migrations/` — schema migration and SQL deployment script
- `wwwroot/` — CSS, JavaScript, and Bootstrap/jQuery assets

## Included workflows

- Identity registration, login/logout, lockout, role-based access, and administrator bootstrap
- Dashboard KPIs and database-backed lead, pipeline, and monthly won-sales charts
- Customer, lead, opportunity, follow-up, and activity management
- Lead conversion and manager/admin lead assignment
- Manager-to-team ownership scoping applied to CRM lists, dashboard, reports, and APIs
- User creation/editing, role assignment, activation/deactivation
- Audit log and customer, lead, follow-up, opportunity, pipeline, conversion, and user-activity reports
- Authenticated REST endpoints under `/api/customers`, `/api/leads`, and `/api/opportunities`

API requests use the Identity authentication cookie. Obtain an anti-forgery request token from `GET /api/antiforgery/token` and send it in the `X-CSRF-TOKEN` header for state-changing requests, together with the anti-forgery cookie.

## Verification

Build the application with:

```powershell
dotnet build
```

There is currently no automated test project in the workspace. Database-backed workflow testing requires a reachable SQL Server configured through `ConnectionStrings__DefaultConnection`.
