
# PokerApp — dokumentacja kodu

  

Dominik Lech

czerwiec 2026

  

---

  

## 1. po co ta dokumentacja

  

projekt ma ~6400 linii C# w jednym assembly. nie opisujemy tu każdej właściwości — skupiamy się na tym **dlaczego** kod jest ułożony tak, a nie inaczej.

pełne komentarze XML (///) są przy **9 klasach/modułach**, które najlepiej pokazują architekturę:

| element | rola |
|---------|------|
| `IGameUi` | kontrakt między silnikiem a UI |
| `MainWindowViewModel` | stan stołu, powtórka, implementacja IGameUi |
| `MainWindow` | pętla turniejowa w widoku |
| `TournamentSession` | adapter turniejowy na NuGet TexasHoldem |
| `LlmBotPlayer` | bot LLM + zapis promptów |
| `OpenAiCompatClient` | HTTP do API |
| `GameSetupConfig` | konfiguracja stołu bez globalnego stanu |
| `HandPersistence` | zapis/odczyt rozdań |
| `PokerDbBootstrap` | init SQLite |
| `ReplayJsonUtil` | metryki z JSON bez pełnego DTO |

reszta plików (np. `PlayerRowVm`, encje EF) jest oczywista z nazw — nie ma sensu dublować IntelliSense.

  

---

  

## 2. architektura w skrócie

  

aplikacja to **monolit desktopowy** — jeden proces, jeden namespace `PokerApp`, brak warstwy serwerowej.

```
Menu (MainMenuWindow)
    → GameSetupConfig
    → MainWindow + MainWindowViewModel
        → TournamentSession → TexasHoldemGameEngine (NuGet)
        → HumanPlayer / RandomBotPlayer / LlmBotPlayer
            → IGameUi (aktualizacja stołu + JSON historii)
    → HandPersistence → SQLite
```

**najważniejsza decyzja:** gracze (`IPlayer`) nie znają Avalonia. dostają `IGameUi` i przez niego:
- aktualizują widok (`RunOnUiThread`),
- zapisują zdarzenia do bufora powtórki (`AppendHandHistory`).

dzięki temu ten sam `LlmBotPlayer` gra na żywo i produkuje JSON do bazy — bez osobnego „loggera rozdania”.

  

**druga decyzja:** historia rozdania to **event log w JSON** (`start_hand`, `action`, `showdown`, `hand_end`). powtórka nie odtwarza gry przez silnik — przewija zapisane kroki. taniej i widać prompty LLM.

  

**trzecia decyzja:** `TournamentSession` używa **refleksji** na typy internal z NuGet. pakiet nie ma publicznego API turniejowego — albo przepisujemy Hold'em od zera, albo akceptujemy kruchość przy aktualizacji silnika.

  

---

  

## 3. przepływy (co gdzie woła)

  

### gra z człowiekiem

1. menu zbiera parametry → `GameSetupConfig`
2. `MainWindow.StartGameLoopAsync` → `InitializeTournament` (raz)
3. pętla: `PlayNextHandAsync` (wątek puli) → overlay zapisu → `HandPersistence.SaveAsync`
4. koniec turnieju → powrót do menu

### seria botów

1. `RunSeriesGameLoopAsync` w menu tworzy `TournamentSeries` w bazie
2. każdy turniej: nowy `MainWindow` ze `SpectatorSeriesMode=true`, shuffle kolejności botów
3. każda ręka: `HandPersistence.SaveSeriesHandAsync` (bez pytania użytkownika)
4. po serii: `TournamentSeriesStats.RecomputeAndSaveAsync`

### powtórka

1. lista ładuje metadane (`MaxPot`, nazwa) — **bez** `hand_history_json`
2. „Otwórz” → `LoadHandHistoryJsonAsync` → `InitializeReplay`
3. użytkownik klika „Dalej” → `AdvanceReplay`

  

---

  

## 4. warstwa danych

  

SQLite w `~/.local/share/PokerApp/poker_app.sqlite` (nadpisanie: `POKERAPP_DB`).

`PokerDbBootstrap` przy starcie:
- sprawdza plik `.ver` (schemat v3),
- przy niezgodności **kasuje** starą bazę i tworzy od nowa,
- `EnsureCreated()` zamiast migracji EF.

to świadomy kompromis na projekt uczelniany — prostsze niż `dotnet ef`, ale użytkownik traci dane przy zmianie schematu.

`max_pot` w `saved_hand` liczymy przy zapisie (`ReplayJsonUtil`) — filtr „ukryj małe pule” działa na liście bez parsowania całego JSON.

  

---

  

## 5. boty LLM

  

`LlmBotPlayer.GetTurn`:
1. `LlmPrompts` składa system + user prompt (osobowość, leksykon, fakty postflop),
2. `OpenAiCompatClient.CompleteChatAsync` — retry do 10×, backoff na 429,
3. parser bierze **ostatnią linię** odpowiedzi jako kod akcji,
4. `prompt_before_action` i `thought_before_action` lądują w JSON — pod analizę w powtórce.

preset (URL, klucz, model) w SQLite; snapshot kopiowany do `GameSetupConfig` na czas gry.

  

---

  

## 6. generowanie dokumentacji XML + HTML

  

### plik XML (IntelliSense + DocFX)

w `PokerApp.csproj` włączone:

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

build:

```bash
cd PokerApp
dotnet build -c Release
```

powstaje `bin/Release/net8.0/PokerApp.xml` z komentarzami ///.

  

### strona HTML (DocFX)

instalacja (raz):

```bash
dotnet tool install -g docfx
```

budowa i podgląd z katalogu projektu:

```bash
./scripts/build-docfx.sh
./scripts/build-docfx.sh --serve
```

przeglądarka: http://localhost:8080

zakładka **api** — klasy z XML. zakładka **artykuły** — ten dokument w skrócie.

  

---

  

## 7. czego świadomie nie ma

  

- testów jednostkowych (logika replay i parsowanie LLM bez pokrycia),
- osobnej warstwy serwisowej — UI woła `PokerDbBootstrap.CreateContext()` bezposrednio,
- migracji EF — tylko `.ver` + recreate,
- multiplayer — wszystko in-process, `IGameUi` nie działa przez sieć.

plany z podręcznika (sieć, tool calling agenta) wymagałyby wydzielenia hosta gry poza Avalonia.

  

---

  

## 8. gdzie szukać dalej

  

- użytkownik końcowy → `PODRECZNIK_UZYTKOWNIKA.md`
- komentarze przy metodach → IntelliSense w IDE albo DocFX → API
- największe pliki do ewentualnego refaktoru: `MainMenuWindow.axaml.cs` (nawigacja + setup), `MainWindowViewModel.cs` (stan stołu)
