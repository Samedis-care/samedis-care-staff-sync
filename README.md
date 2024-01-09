# samedis-care-staff-sync

.Net Core example project to read excel file and insert or update staff records.
You can fork with project and modify it to your own needs.

## Setup

1. Copy and modify `config.yml.example` to `config.yml`
2. Adjust settings in `config.yml`
3. Compile the application to your target OS, modify `SamedisStaffSync.csproj` to your requirements
   - Follow https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview?tabs=cli for more details on compile and deploy
4. Run the application manually or setup a `cron task` or `task` on windows systems.

## Excel Import file

The Excel format matches exactly the excel file Samedis.care creates on an export.
The file `import/import.xlsx` contains one example.

> You may export and add the column `Id` if your want to export, modify and import your staff lists inside Samedis.care
