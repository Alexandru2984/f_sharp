# Finance Anomaly Detector

A multi-tenant F# (.NET 8) backend utilizing SQLite and Giraffe to track expenses, manage budgets, and detect fraudulent or anomalous spending patterns based on historical user behavior. The frontend is built natively with HTML/CSS/JS and Chart.js.

## Features

- **Multi-Tenant Architecture**: Users have completely isolated data spaces. You can register an account or use the default admin account.
- **Budget Tracking**: Set limits on expense categories and track spending via intuitive progress bars.
- **Rule-Based Anomaly Engine**: Automatically detects suspicious spikes, miscategorizations, and late-night expenses. Anomalies can be marked as resolved.
- **Dashboard & Trends**: Visualizes data through charts to indicate current monthly standing, highest risk categories, and trend spending over time.
- **CSV Data Import**: Quickly seed your account via standard CSV files.

## Running the App

### Requirements
- .NET 8.0 SDK
- Node.js (Optional, only for asset management)

### Startup

```bash
# Navigate to the project root
cd f_sharp

# Run the app
dotnet run --project src/FinanceAnomalyDetector/FinanceAnomalyDetector.fsproj
```
The server will start listening on `http://localhost:5000`.

### Database
The app uses SQLite (`data/finance.db`). Running the app will auto-migrate and seed a default user:
**Username:** `admin`  
**Password:** `admin123`

### Security
- Passwords are hashed using BCrypt.
- Sessions are managed securely via HttpOnly ASP.NET Core cookies.
- Direct JSON parameter binding via Dapper for database safety.