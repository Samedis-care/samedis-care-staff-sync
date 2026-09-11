#!/usr/bin/env python3
"""
Builds the employee import staff-sync reads in `csv` mode, as a stand-in for a real HR
export and without real names.

It deliberately builds on what external-sync imported: the `Abteilungstext` values are the
eight department titles from that fixture set, so the employees attach to departments that
already exist instead of creating a second set. Run the external-sync import first.

  5 positions   Applikationsbetreuer, MPB, MPV, IT, Medizintechnik
  30 employees  each with one position and one department
"""
import csv, io, pathlib

OUT = pathlib.Path(__file__).parent / "import"
OUT.mkdir(parents=True, exist_ok=True)

# Mandatory: Vorname, Nachname, Mitarbeiternr., "Beitritt am", "Austritt am".
# The rest are optional and read when present.
HEADER = ["Vorname", "Nachname", "Mitarbeiternr.", "Beitritt am", "Austritt am", "E-Mail",
          "Titel", "Bemerkungen", "Handynummer", "Positionen", "Abteilungen",
          "Abteilungstext", "Kostenstelle"]

POSITIONS = ["Applikationsbetreuer", "MPB", "MPV", "IT", "Medizintechnik"]

# (key, title, cost centre) -- the titles are external-sync's departments, so these resolve
# to the records that import created rather than to new ones.
DEPARTMENTS = [
    ("41100", "Innere Medizin",  "41100"),
    ("41110", "Kardiologie",     "41110"),
    ("41200", "Chirurgie",       "41200"),
    ("41210", "Unfallchirurgie", "41210"),
    ("41300", "Radiologie",      "41300"),
    ("41310", "Nuklearmedizin",  "41310"),
    ("41400", "Anaesthesie",     "41400"),
    ("41500", "Zentrallabor",    "41500"),
]

FIRST = ["Anna", "Bernd", "Clara", "David", "Elena", "Frank", "Greta", "Hakan", "Ines",
         "Jonas", "Katrin", "Lars", "Maria", "Nils", "Olga", "Peter", "Quirin", "Rosa",
         "Sven", "Tanja", "Ulrich", "Vera", "Wilhelm", "Xenia", "Yannick", "Zoe",
         "Amelie", "Bastian", "Carla", "Dennis"]
LAST = ["Ahrens", "Berger", "Conrad", "Dietrich", "Engel", "Fischer", "Gruber", "Haas",
        "Iversen", "Jansen", "Keller", "Lindner", "Mayer", "Neumann", "Ortmann", "Pfeiffer",
        "Quast", "Richter", "Schuster", "Thiel", "Ulrich", "Vogel", "Wagner", "Xander",
        "Yilmaz", "Zimmer", "Albrecht", "Brandt", "Clemens", "Dorn"]
TITLES = ["", "Dr.", "", "", "Prof. Dr.", ""]

rows = []
for i in range(30):
    first, last = FIRST[i], LAST[i]
    position = POSITIONS[i % len(POSITIONS)]
    dept_key, dept_title, cost_center = DEPARTMENTS[i % len(DEPARTMENTS)]

    # Two employees have left, so the retired-staff path is covered as well. The rest carry
    # an empty leaving date, which is what the import expects for someone still employed.
    left = "31.03.2026" if i in (7, 22) else ""

    rows.append([
        first,
        last,
        f"P{4200 + i * 3}",
        f"{1 + i % 28:02d}.{1 + i % 12:02d}.{2012 + i % 12}",
        left,
        f"{first.lower()}.{last.lower()}@musterklinik.example",
        TITLES[i % len(TITLES)],
        "" if i % 3 else f"Bemerkung zu Mitarbeiter {i + 1}",
        f"+49 151 {1000000 + i * 37}",
        position,
        dept_key,
        dept_title,
        cost_center,
    ])

# A few awkward rows, so the edge paths stay covered.
rows[4][0] = "  Elena  "                     # padded value
rows[9][5] = ""                              # no email
rows[14][8] = ""                             # no mobile number
rows[19][6] = "Dr. med. dent."               # a longer title

buf = io.StringIO()
w = csv.writer(buf, delimiter=",", lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
w.writerow(HEADER)
w.writerows(rows)
(OUT / "test_daten.csv").write_text(buf.getvalue(), encoding="utf-8", newline="")

print(f"  test_daten.csv     {len(rows)} employees")
print(f"  Positionen         {len(POSITIONS)}: {', '.join(POSITIONS)}")
print(f"  Abteilungen        {len(DEPARTMENTS)} (Titel aus dem external-sync-Import)")
print(f"\n  -> {OUT}")
