-- Column names has need to have this exact match
-- "Nachname", "Vorname", "Mitarbeiternr.", "Beitritt am", "Austritt am", "E-Mail", "Handynummer", "Titel", "Bemerkungen"
SELECT [Nachname]
  , [Vorname]
  , [Mitarbeiternr] AS [Mitarbeiternr.]
  , [Beitritt am]
  , [Austritt am]
  , [E-Mail]
  , [Handynummer]
  , [Titel]
  , [Bemerkungen]
FROM StaffImport;