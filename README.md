# Narzędzie do metadanych Firebird 5.0

Konsolowa aplikacja w **.NET 8**, która:

- eksportuje metadane z istniejącej bazy Firebird 5.0 do plików `.sql`,
- buduje nową bazę na podstawie wygenerowanych skryptów,
- aktualizuje istniejącą bazę na podstawie skryptów.

Zakres uproszczony – obsługiwane są:
- **domeny**,
- **tabele** (z kolumnami),
- **procedury**.

Pozostałe obiekty (indeksy, triggery, FK, itp.) są pomijane.

---

## 1. Wymagania

- **.NET 8 SDK**
- **Firebird 5.0**
- Biblioteka: `FirebirdSql.Data.FirebirdClient`
- Plik `fbclient.dll` dostępny w systemie (np. w katalogu Firebirda) lub obok pliku `.exe`

Dostęp do baz:
- dla **export-scripts** i **update-db** – connection string podawany w argumencie lub w `.env`,
- dla **build-db** – użytkownik i hasło do stworzenia nowej bazy z `.env`.

---

## 2. Konfiguracja – plik `.env`

W katalogu projektu umieszczamy plik `.env` (nie jest versionowany w git).

Przykład:

```env
FB_NEW_DB_USER="SYSDBA"
FB_NEW_DB_PASSWORD="TwojeHasloDoFirebird"

CONNECTION_STRING="Database=C:\FB\Source.fdb;User=SYSDBA;Password=TwojeHasloDoFirebird;DataSource=localhost;Port=3050;Dialect=3;ServerType=0;ClientLibrary=fbclient.dll;"
```

Znaczenie:

- `FB_NEW_DB_USER`, `FB_NEW_DB_PASSWORD` – login i hasło używane przy tworzeniu **nowej** bazy (`build-db`).  
  Jeśli nie zostaną ustawione, `build-db` zakończy się błędem logowania do Firebirda.
- `CONNECTION_STRING` – domyślny connection string używany przez:
  - `export-scripts`
  - `update-db`  
  jeśli **nie** zostanie podany jawnie parametr `--connection-string`.

Plik `.env` jest **zalecany**, ale:
- `export-scripts` i `update-db` mogą działać bez `.env`, jeśli podasz connection string w argumencie,
- `build-db` wymaga użytkownika/hasła z `.env` (nie ma żadnego „domyślnego” loginu w kodzie).

---

## 3. Uruchamianie – sposób wywołania

W trybie developerskim (z poziomu katalogu projektu) komendy uruchamiam przez:

```bash
dotnet run -- <komenda> [parametry]
```

### Przykłady:

```bash
dotnet run -- export-scripts --output-dir "C:\FB\Meta\SourceScripts"

dotnet run -- build-db --db-dir "C:\FB\Target\database" --scripts-dir "C:\FB\Meta\SourceScripts"

dotnet run -- update-db --connection-string "Database=C:\FB\Target\database.fdb;User=SYSDBA;Password=...;DataSource=localhost;Port=3050;Dialect=3;ServerType=0;ClientLibrary=fbclient.dll;" --scripts-dir "C:\FB\MetaUpdates"
```

---

## 4. Komendy

### 4.1. Eksport metadanych – `export-scripts`

Eksportuje metadane z istniejącej bazy Firebird 5.0.

```bash
dotnet run -- export-scripts --output-dir "C:\FB\Meta\SourceScripts"
```

Skąd bierze connection string?

1. jeśli podasz `--connection-string "..."` – użyje go,
2. jeśli **nie** podasz parametru, spróbuje użyć `CONNECTION_STRING` z `.env`,
3. jeśli oba źródła są puste – wyrzuci czytelny błąd:

   ```text
   Brak parametru --connection-string oraz wartości CONNECTION_STRING w pliku .env
   ```

Struktura wyjściowa:

```text
C:\FB\Meta\SourceScripts\
  domains\   *.sql   (CREATE DOMAIN ...)
  tables\    *.sql   (CREATE TABLE ...)
  procedures\*.sql   (CREATE OR ALTER PROCEDURE ...)
```

**Co jest eksportowane?**

- Domeny:
  - pomijane domeny systemowe `RDB$...`
  - poprawne mapowanie typów (VARCHAR z długością znaków, NUMERIC/DECIMAL z precision/scale)
- Tabele:
  - nazwy kolumn
  - typy (w oparciu o domeny użytkownika lub implicit domeny mapowane na typy)
  - NOT NULL
- Procedury:
  - parametry IN/OUT
  - źródło procedury
  - generowany jest `CREATE OR ALTER PROCEDURE ...`

---

### 4.2. Budowa nowej bazy – `build-db`

Tworzy **nową** bazę Firebird 5.0 na podstawie wygenerowanych skryptów.

```bash
dotnet run -- build-db --db-dir "C:\FB\TargetFinal\database" --scripts-dir "C:\FB\Meta\SourceScripts"
```

Uwaga:  
`--db-dir` oznacza tutaj **pełną ścieżkę do pliku bazy BEZ lub Z `.fdb`**:

- jeśli przekażesz `C:\FB\TargetFinal\database` → aplikacja utworzy `C:\FB\DB\TargetFinal\database.fdb`
- jeśli przekażesz `C:\FB\TargetFinal\moja_baza.fdb` → aplikacja użyje dokładnie takiej ścieżki

Aplikacja:

1. tworzy pustą bazę (`FbConnection.CreateDatabase(...)`),
2. łączy się z nową bazą przy użyciu:
   - `FB_NEW_DB_USER` i `FB_NEW_DB_PASSWORD` z `.env`,
   - `DataSource = localhost`
   - `Port = 3050` (na sztywno w kodzie – **nowe bazy zawsze powstają na 3050**),
3. wykonuje kolejno skrypty:
   - `domains\*.sql`
   - `tables\*.sql`
   - `procedures\*.sql`

> Port i host dla nowej bazy są ustawione w kodzie na `localhost:3050`.  
> Jeśli chcesz używać innego portu/hosta przy **budowie** nowej bazy – wymaga to zmiany kodu.

---

### 4.3. Aktualizacja istniejącej bazy – `update-db`

Wykonuje skrypty aktualizacyjne na istniejącej bazie Firebird 5.0.

Przykład:

```bash
dotnet run -- update-db --connection-string "Database=C:\FB\TargetFinal\database.fdb;User=SYSDBA;Password=...;DataSource=localhost;Port=3050;Dialect=3;ServerType=0;ClientLibrary=fbclient.dll;" --scripts-dir "C:\FB\MetaUpdates"
```

lub bez parametru `--connection-string`, jeśli jest ustawiony `CONNECTION_STRING` w `.env`:

```bash
dotnet run -- update-db --scripts-dir "C:\FB\MetaUpdates"
```

Struktura katalogu aktualizacji:

```text
C:\FB\MetaUpdates\
  domains\
  tables\
  procedures\
```

Aplikacja:

- łączy się z bazą na podstawie connection stringa (tu można zmienić **host** i **port** dowolnie),
- wykonuje wszystkie `.sql` w katalogach:
  - `domains`,
  - `tables`,
  - `procedures`,
- jeśli w którymś skrypcie jest błąd (np. próbujesz dwa razy dodać tę samą kolumnę) – pokazuje nazwę pliku i treść błędu z Firebirda.

> Uwaga: `update-db` nie wykonuje „diffów” schematu.  
> Skrypty są uruchamiane 1:1 – jeśli wykonasz ten sam `ALTER TABLE ... ADD` dwa razy, za drugim razem Firebird zwróci błąd „kolumna już istnieje”. Jest to zachowanie świadome.

---

## 5. Zachowanie w przypadku braku `.env`

- Jeśli **nie ma `.env`**, ale podasz `--connection-string`:
  - `export-scripts` – działa,
  - `update-db` – działa.

- Jeśli **nie ma `.env`** i **nie podasz `--connection-string`**:
  - `export-scripts` / `update-db` – zakończą się błędem z komunikatem:
    - `Brak parametru --connection-string oraz wartości CONNECTION_STRING w pliku .env`

- Jeśli **nie ma (lub są puste) `FB_NEW_DB_USER` / `FB_NEW_DB_PASSWORD`**:
  - `build-db` – nie będzie w stanie zalogować się do Firebirda przy tworzeniu nowej bazy  
    (błąd uwierzytelnienia po stronie Firebirda).

To jest zachowanie **świadome** – użytkownik musi:

- albo skonfigurować `.env`,
- albo jawnie podawać connection string dla operacji `export-scripts` i `update-db`,
- oraz mieć skonfigurowanego użytkownika i hasło dla `build-db`.

---

## 6. Bezpieczeństwo

- Dane logowania są trzymane w `.env`, który nie jest commitowany do repo.
- Connection stringi można podawać z linii komend tylko, jeśli użytkownik świadomie tak chce.

---

## 7. Zakres rozwiązania

- zaimplementowano trzy główne operacje:
  - `build-db`
  - `export-scripts`
  - `update-db`
- obsługiwane obiekty:
  - domeny,
  - tabele (z polami),
  - procedury,
- pominięte zostały:
  - triggery, constraints, indeksy, widoki, FK, sekwencje itp.

Narzędzie testowane na rzeczywistej bazie Firebird 5.0, zgodnie ze scenariuszem:

1. stworzenie bazy źródłowej,
2. eksport metadanych,
3. budowa nowej bazy z eksportu,
4. aktualizacja na podstawie skryptu,
5. porównanie struktur w narzędziu GUI (IBExpert).
