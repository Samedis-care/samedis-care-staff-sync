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

You can choose Ldap (Active Directory), Excel or a SQL server as source.

```
import_mode: "sql"
```

Enter `ldap`, `excel` or `sql` as mode. Regarding your chosen mode you need to adjust the configuration file in the excel or sql section.

## Excel Import file

The Excel format matches exactly the excel file Samedis.care creates on an export. The file `import/import.xlsx` contains one example.

> You may export and add the column `Id` if your want to export, modify and import your staff lists inside Samedis.care

## Sql server import

You can configure and setup a source to get your data from a sql server source.
Instead of using and excel file you can modify `import/_export_staff.sql` to your own requirements.

> It is mandatory that the column names match the names of the sql example query.

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

For example if you want to read your employee number from the field `extensionAttribute4` and fill the Samedis.care `notes` field from the active directory `info` field instead of the description field, then the mapping would ne like this:

```
  mapping: "{ \"Nachname\": \"sn\", \"Vorname\": \"givenName\", \"Personalnummer\": \"extensionAttribute4\", \"Eintrittsdatum\": \"whenCreated\", \"Austrittsdatum\": \"extensionAttribute10\", \"E-Mail\": \"mail\", \"Titel\": \"title\", \"Bemerkungen\": \"info\", \"Handynummer\": \"mobile\" }"
```

**Note:** If you do deactivate the user in your active directory we will set the `left` date to the timestamp of the sync as alternative, if you can not provide a proper field (in our example it is `extensionAttribute10`) to define the date the employee left.

