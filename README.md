# Limit czasu przy komputerze

Aplikacja dla Windows 10/11, która odlicza czas pracy przy komputerze, ostrzega przed końcem sesji
i po 30 minutach przysłania ekran na 30-minutową przerwę.

## Jak to działa

| Wymaganie | Realizacja |
|---|---|
| Start przy logowaniu | Usługa Windows (SYSTEM, start automatyczny) uruchamia aplikację w zasobniku w sesji każdego zalogowanego użytkownika z listy kont objętych limitem |
| Wybór kont | W ustawieniach (tray → „Ustawienia (administrator)…") zaznaczasz konta, dla których aplikacja ma się uruchamiać i liczyć czas. Niezaznaczone konta nie są ani blokowane, ani liczone |
| 30 minut po zalogowaniu | Nowa sesja trwa 30 min (`SessionMinutes` w konfiguracji) |
| Czas w zasobniku | Ikona w zasobniku pokazuje liczbę pozostałych minut (poniżej minuty sekundy); kolor: niebieski, pomarańczowy (≤ 5 min), czerwony (ostrzeżenie), zielony (przerwa). Pełny czas jest w podpowiedzi |
| Ostrzeżenie | Na 2 minuty przed końcem pojawia się okno z odliczaniem i przypomnieniem o zapisaniu zmian |
| Blokada ekranu | Nieprzezroczysta nakładka na wszystkich monitorach, zawsze na wierzchu; blokuje Win, Alt+Tab, Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc, Alt+F4 |
| Przedłużenie | Kod + przycisk +5 / +10 / +15 min. Po 3 błędnych kodach rosnąca blokada prób (15 s, 30 s, … do 5 min) |
| Przełączenie konta | Przycisk „Zablokuj / przełącz konto" (systemowe Zablokuj, Win+L) |
| Odliczanie przerwy | Na ekranie blokady widać czas do końca 30-minutowej przerwy |
| Wylogowanie w trakcie przerwy | Przerwa liczy się zegarem rzeczywistym, także po wylogowaniu i restarcie. Po powrocie przed jej końcem blokada wraca z dalszym odliczaniem |
| Kod | Zapisywany jako hasz PBKDF2 w `%ProgramData%\ScreenTimer\config.json`. Zmiana: tray → „Ustawienia (administrator)…", wymaga UAC |

### Reguły liczenia czasu
- Stan jest osobny dla każdego konta Windows.
- Czas sesji biegnie tylko, gdy konto jest aktywne. Blokada ekranu, wylogowanie, przełączenie na inne konto i uśpienie go wstrzymują.
- Nieobecność trwająca co najmniej tyle co przerwa (30 min) liczy się jako odpoczynek: po powrocie zaczyna się pełna nowa sesja. Krótsza nieobecność tylko wstrzymuje licznik.
- Po przedłużeniu o 5/10/15 min, a potem upływie tego czasu, następuje kolejna pełna przerwa.

### Ochrona przed obejściem
- Stan, odliczanie i weryfikacja kodu są w usłudze SYSTEM. Aplikacja w zasobniku tylko wyświetla.
- Jeśli aplikację uda się zamknąć, usługa uruchamia ją ponownie w ciągu kilku sekund (sygnał życia przez potok nazwany).
- Proces aplikacji ma ACL, który uniemożliwia kontu bez uprawnień administratora zakończenie go (sprawdzone: „Odmowa dostępu").
- Podczas przerwy usługa zamyka Menedżera zadań na koncie objętym limitem.
- Katalog danych (kod, stan) jest dostępny tylko dla SYSTEM i Administratorów.

Ograniczenia: Ctrl+Alt+Del obsługuje Windows i nie da się go zablokować (z tego ekranu da się wylogować, ale przerwa dalej biegnie). Konto z uprawnieniami administratora może zatrzymać usługę, więc ochrona jest skuteczna dla kont standardowych.

## Instalacja z pakietu MSI (zalecana)

Z wydania pobierz plik dla swojej architektury: `ScreenTimer-<wersja>-win-x64.msi` (zwykły PC), `win-arm64` (np. Surface Pro X, Copilot+ PC) lub `win-x86` (32-bitowy Windows). Uruchom go; instalator poprosi o uprawnienia administratora, zainstaluje usługę w `Program Files\ScreenTimer` i otworzy ustawienia, w których wybierasz konta objęte limitem i ustawiasz kod. Później ustawienia otworzysz skrótem „Limit czasu – ustawienia" w menu Start. Instalacja bez okien: `msiexec /i ScreenTimer-0.0.1-win-x64.msi /qn`. Aktualizacja to uruchomienie nowszego MSI. Odinstalowanie: Ustawienia → Aplikacje (kod i konfiguracja w `%ProgramData%\ScreenTimer` zostają).

Pakiety ZIP z tych samych wydań zawierają folder `app\` oraz skrypty `install.ps1` i `uninstall.ps1` (opis poniżej).

## Instalacja ze skryptów

Wymagane: PowerShell jako administrator. Na docelowym komputerze nie trzeba instalować .NET.

```powershell
.\scripts\build.ps1                                   # publikacja do dist\publish (x64 lub arm64 wg bieżącego komputera)
.\scripts\install.ps1 -LimitedAccount dziecko1,dziecko2   # instalacja usługi; konta objęte limitem (opcjonalnie)
```

Domyślnie żadne konto nie jest objęte limitem, więc świeża instalacja nikogo nie zablokuje, dopóki nie wybierzesz kont (parametrem `-LimitedAccount` albo w ustawieniach). Konta zapisywane są jako SID, więc zmiana nazwy konta niczego nie psuje.

Instalator otworzy okno ustawień, jeśli kod lub konta nie są jeszcze ustawione. Bez kodu nie da się przedłużyć przerwy.

Odznaczenie konta kasuje jego zapisany stan (sesję i przerwę); po ponownym zaznaczeniu zaczyna od pełnej sesji.

Odinstalowanie: `.\scripts\uninstall.ps1` (z `-RemoveData` usuwa też kod i stan).

## Konfiguracja

`%ProgramData%\ScreenTimer\config.json` (edycja tylko jako administrator, usługa wczytuje zmiany sama):

```json
{
  "SessionMinutes": 30,
  "BreakMinutes": 30,
  "WarningMinutes": 2,
  "ExtensionMinutes": [5, 10, 15],
  "LimitedAccounts": [{ "Sid": "S-1-5-21-...", "Name": "PC\\dziecko1" }]
}
```

`LimitedAccounts` najwygodniej zmieniać w oknie ustawień. Starsze pole `ExemptAccounts` nie jest już używane.

Dziennik usługi: `%ProgramData%\ScreenTimer\service.log`. Dziennik błędów interfejsu: `%LOCALAPPDATA%\ScreenTimer\app.log`.

## Budowanie wydania

```powershell
.\scripts\release.ps1 -Version 0.0.1     # dist\release: MSI i ZIP dla win-x64, win-arm64 i win-x86
```

Skrypt sam pobiera WiX 5 do `.tools\` (poza gitem). MSI definiuje `installer\Package.wxs`.

## Rozwój

SDK .NET 8 jest w `.dotnet\` (lokalnie, poza gitem). Użyj `.\.dotnet\dotnet.exe` albo własnego SDK.

```powershell
.\.dotnet\dotnet.exe test                               # testy logiki odliczania i kodu
$env:DOTNET_ROOT = "$PWD\.dotnet"                       # tylko przy uruchamianiu buildu Debug z lokalnego SDK
.\src\ScreenTimer\bin\Debug\net8.0-windows\ScreenTimer.exe --tray --dev --exit-after 70
```

Tryb `--dev` uruchamia samą aplikację bez usługi, ze skróconymi czasami (sesja 36 s, ostrzeżenie po 18 s, przerwa 36 s, kod `1234`).
W tym trybie nakładkę zamyka klawisz Esc, a skróty klawiszowe nie są blokowane.

Struktura: `ScreenTimer.Core` (silnik czasu, konfiguracja, protokół, bez zależności od Windows), `ScreenTimer` (usługa, potok, interfejs WPF), `tests` (xUnit).
