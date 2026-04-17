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

You can choose LDAP (Active Directory), SAP HR CSV export, CSV, Excel or a SQL server as source.

```
import_mode: "sql"
```

Enter `ldap`, `sap`, `csv`, `excel` or `sql` as mode. Regarding your chosen mode you need to adjust the configuration file in the excel or sql section.

## Options

The `options` section controls how staff, positions and departments are synced:

```
options:
  create_positions: false
  create_departments: false
  login_allowed: false
```

- `create_positions`: if `true`, positions referenced in the import that do not exist yet in Samedis.care are created automatically. If `false`, only existing positions are looked up and linked.
- `create_departments`: same behavior for departments (including code and cost center from the import, if available).
- `login_allowed`: if `true`, the staff payload sent to the Samedis.care API includes `login_allowed: true` so newly created or updated staff records are flagged to be allowed to log in. Leave `false` if login access should be granted manually in Samedis.care.

## Test mode

If you want to check which final data would be transmitted to Samedis.care via the API, you can set `testing.active: true`.
Staff API calls will then write the data to a CSV file `test_output.csv`, which you can check for content.
If the data is correct, you can switch off test mode again (set value to `false`) and run the process again.

## CSV / Excel Import file

The CSV or Excel format headers are similar to the Excel template you can download from the Samedis.care import form. The file `import/import.xlsx` (or `import.csv`) contains one example. The default headers are:

```
Vorname;Nachname;Mitarbeiternr.;Beitritt am;Austritt am;E-Mail;Titel;Bemerkungen;Handynummer
```

Optional columns `Positionen` and `Abteilungen` can be added to link a staff record to a position and/or department (they must already exist in Samedis.care, or `create_positions` / `create_departments` must be set to `true`).

> Note: In the rare case that you want to change the personnel number, you have to export all employees from Samedis.care as Excel with the Samedis.care id. You can then make an import with the Excel column `Id`, which uses this column as an identifier for inserting or updating. The personnel number is then overwritten.

## SAP CSV Import file

This mode implements a SAP HR CSV export. Expected columns (case sensitive, several aliases are tolerated — see [HelperSap.cs](HelperSap.cs)):

```
Personalnummer,Vorname,Nachname,Titel,E-Mail,Handynummer,Bemerkungen,Eintritt,Austritt,Abteilung,Abteilungstext,Kostenstelle,Position,Dienstart,Dienstart Text
```

As the records of this export might contain an employee several times (join, leave, switch departments, ...) the data records are consolidated by `Personalnummer` and a single final record per employee is generated. The latest `Eintritt` wins as representative; `Austritt` is only used when it is more recent than the latest `Eintritt`.

## Sql server import

You can configure and setup a source to get your data from a sql server source.
Instead of using an Excel file you can modify `import/import.sql` to your own requirements.

> It is mandatory that the column names returned by the query match the column names of the CSV/Excel import (see above).

## Ldap server import

If you want to get your staff users from an active directory (LDAP) server this is supported now, too.
In this case you do have to setup the `config.yml` with the correct parameters

Most of the parameters in `config.yml.example` are self-explanatory. However, the values `filter` and `mapping` must be explained.

```
  filter: "(&(objectCategory=person)(objectClass=user))"
```

This will be your filter what elements will be imported. Here you can add more filters, for example if you like to import only those users where `extensionAttribute4` is filled the filter would be like

```
  filter: "(&(objectCategory=person)(objectClass=user)(extensionAttribute4=*))"
```

With `Mapping` you can specify/change the fields of your directory server that are to be imported.
The example file contains the default mapping, and it is **mandatory** that all fields are mapped.

For example if you want to read your employee number from the field `extensionAttribute4` and fill the Samedis.care `notes` field from the active directory `info` field instead of the description field, then the mapping would be like this:

```
  mapping: "{ \"Nachname\": \"sn\", \"Vorname\": \"givenName\", \"Mitarbeiternr.\": \"extensionAttribute4\", \"Beitritt am\": \"whenCreated\", \"Austritt am\": \"extensionAttribute10\", \"E-Mail\": \"mail\", \"Titel\": \"title\", \"Bemerkungen\": \"info\", \"Handynummer\": \"mobile\" }"
```

**Note:** If you do deactivate the user in your active directory we will set the `left` date to the timestamp of the sync as alternative, if you can not provide a proper field (in our example it is `extensionAttribute10`) to define the date the employee left.

