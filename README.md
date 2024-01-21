# samedis-care-staff-sync

.Net Core project to read excel file or query from any sql server source and insert or update staff records.
You can fork with project and modify it to your own needs.

## Setup

1. Copy and modify `config.yml.example` to `config.yml`
2. Adjust settings in `config.yml`
3. Compile the application to your target OS, modify `SamedisStaffSync.csproj` to your requirements
   - Follow https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview?tabs=cli for more details on compile and deploy
4. Run the application manually or setup a `cron task` or `task` on windows systems.

## Proxy server support

To access Samedis.care you need internet access. If this requires a proxy server you can configure your settings in the `config.yml` section of `http`.

```
  proxy: ""
  proxy_username: ""
  proxy_password: ""
```

## Import mode

You can choose Excel or a sql server as source.

```
import_mode: "sql"
```

Enter `excel` or `sql` as mode. Regarding your chosen mode you need to adjust the configuration file in the excel or sql section.

## Excel Import file

The Excel format matches exactly the excel file Samedis.care creates on an export. The file `import/import.xlsx` contains one example.

> You may export and add the column `Id` if your want to export, modify and import your staff lists inside Samedis.care

## Sql server import

You can configure and setup a source to get your data from a sql server source.
Instead of using and excel file you can modify `import/_export_staff.sql` to your own requirements.

> It is mandatory that the column names match the names of the sql example query.
