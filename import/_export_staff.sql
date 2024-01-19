-- Column names has need to have this exact match
-- "Nachname", "Vorname", "Personalnummer", "Eintrittsdatum", "Austrittsdatum", "E-Mail", "Titel", "Bemerkungen", "Handynummer"
SELECT Vorname
  , Nachname
  , Mitarbeiternr AS Personalnummer
  , [E-Mail]
  , Titel
  , Notizen AS Bemerkungen
  , Handynummer
  , [Beitritt am] AS Eintrittsdatum
  , [Austritt am] AS Austrittsdatum
FROM StaffImport LIMIT 1000;