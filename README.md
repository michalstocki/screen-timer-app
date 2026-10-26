# Limit czasu przy komputerze

Aplikacja dla Windows 10/11, która odlicza czas pracy przy komputerze, ostrzega przed końcem sesji
i po 30 minutach przysłania ekran na 30-minutową przerwę.

## Jak to działa

| Wymaganie | Realizacja |
|---|---|
| Start przy logowaniu | Usługa Windows (SYSTEM, start automatyczny) uruchamia aplikację w zasobniku w sesji każdego zalogowanego użytkownika |
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

## Instalacja

Wymagane: PowerShell jako administrator. Na docelowym komputerze nie trzeba instalować .NET.

```powershell
.\scripts\build.ps1                                   # publikacja do dist\publish (x64 lub arm64 wg bieżącego komputera)
.\scripts\install.ps1 -ExemptAccount rodzic           # instalacja usługi; konta wyłączone z limitu
```

**Uwaga:** każde konto, którego nie wymienisz w `-ExemptAccount`, podlega limitowi, także konto administratora, z którego instalujesz. Wpisz tam swoje konto, jeśli nie chcesz sam dostać blokady po 30 minutach.

Instalator otworzy okno ustawień, jeśli kod nie jest jeszcze ustawiony. Bez kodu nie da się przedłużyć przerwy.

Odinstalowanie: `.\scripts\uninstall.ps1` (z `-RemoveData` usuwa też kod i stan).

## Konfiguracja

`%ProgramData%\ScreenTimer\config.json` (edycja tylko jako administrator, usługa wczytuje zmiany sama):

```json
{
  "SessionMinutes": 30,
  "BreakMinutes": 30,
  "WarningMinutes": 2,
  "ExtensionMinutes": [5, 10, 15],
  "ExemptAccounts": ["rodzic"]
}
```

Dziennik usługi: `%ProgramData%\ScreenTimer\service.log`. Dziennik błędów interfejsu: `%LOCALAPPDATA%\ScreenTimer\app.log`.

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
