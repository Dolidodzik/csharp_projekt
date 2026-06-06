# architektura PokerApp

## warstwy

aplikacja desktopowa, jeden proces. nie ma backendu — SQLite lokalnie, LLM przez HTTP tylko gdy bot typu LLM gra.

| warstwa | katalog | odpowiedzialność |
|---------|---------|------------------|
| menu | `MainMenu/` | setup gry, powtórki, CRUD presetów |
| stół | `Game/` | widok, ViewModel, pętla turniejowa |
| gracze | `Agents/` | HumanPlayer, RandomBotPlayer, LlmBotPlayer |
| dane | `Database/` | EF Core, encje, zapis rąk |
| wspólne | root | TournamentSession, ReplayJsonUtil, GameSetupConfig |

## IGameUi — dlaczego istnieje

silnik NuGet woła `IPlayer` z wątku roboczego. Avalonia wymaga Dispatcher. `IGameUi` to most:

- gracz woła `SetPot`, `SetHoleCards` itd.
- ViewModel robi `Dispatcher.UIThread.Post`
- przy okazji dopisuje zdarzenia do bufora JSON

bez tego interfejsu albo gracze znałyby Avalonia, albo powtórka byłaby osobnym systemem logowania.

## TournamentSession

opakowuje wiele rozdań w jeden turniej. używa refleksji na `InternalPlayer` i `HandLogic` z NuGet — nie ma publicznego API turniejowego.

`escalatingBlinds=true` tylko w serii botów (`GameSetupConfig.SpectatorSeriesMode`).

## powtórka

JSON w `saved_hand.hand_history_json`. typy zdarzeń: `replay_header`, `start_hand`, `start_round`, `action`, `showdown`, `hand_end`.

`MainWindowViewModel.InitializeReplay` buduje listę kroków. `AdvanceReplay` przewija — silnik nie jest uruchamiany ponownie.

w powtórce wszystkie karty widoczne od początku (wymaganie produktowe).

## baza

`PokerDbBootstrap` — plik `.ver` obok sqlite. zmiana wersji = nowa baza.

`HandPersistence` — jedyny punkt zapisu rąk z gry. menu nie pisze SQL samo.

## dokumentowane klasy

komentarze XML (///) celowo tylko przy reprezentatywnych typach — reszta z nazw metod i IntelliSense. szczegóły w zakładce **API**.
