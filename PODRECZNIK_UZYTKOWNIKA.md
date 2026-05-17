
# PokerApp — podręcznik projektu

  

Dominik Lech

17 maja 2026

  

---

  

## 1. Temat projektu

  

PokerApp to aplikacja desktopowa do gry w Texas Hold’em przeciwko botom oraz do odtwarzania zapisanych rozdań. Użytkownik może grać sam, albo uruchomić serię turniejów wyłącznie między botami (tryb obserwatora), żeby testować boty oparte o modele językowe z własnymi osobowościami i presetami API. Aplikacja zapisuje historię rozdań w bazie SQLite i pozwala je później przeglądać krok po kroku.

Wyróżnia się wykorzystaniem modeli językowych do gry w pokera, i zapewnieniem interfejsu pozwalającego na analizę różnych modeli językowych pod tym kątem, albo na testowanie pytań które  model może sobie zadawać podczas podejmowania decyzji, tak żeby podjąć jak najlepszą. 
Taka rozgrywka z botami o wiele bardziej przypomina "ludzką" grę, niż gra z botami, które podejmują decyzję zgodne/zbliżone do nash equilibrium.
  
  

## 2. Uruchomienie projektu (developer)

  

### 2.1 Wykorzystane technologie

  

| Technologia | Wersja | Rola | Dokumentacja |

|-------------|--------|------|--------------|

| .NET SDK | 8.0 | język, runtime, narzędzia build | https://dotnet.microsoft.com/download/dotnet/8.0 |

| C# | 12 (z .NET 8) | logika aplikacji | https://learn.microsoft.com/dotnet/csharp/ |

| Avalonia UI | 12.0.1 | interfejs desktop (Windows, Linux, macOS) | https://avaloniaui.net/ |

| Material.Avalonia | 3.16.1 | motyw wizualny | https://github.com/AvaloniaCommunity/Material.Avalonia |

| Entity Framework Core | 8.0.11 | ORM, SQLite | https://learn.microsoft.com/ef/core/ |

| SQLite | 3 (wbudowany w EF) | baza lokalna | https://www.sqlite.org/ |

| TexasHoldemGameEngine | 2.0.0 | silnik zasad Hold’em | pakiet NuGet |

| Svg.Skia | 2.0.0.4 | render kart SVG | https://github.com/wieslawsoltes/Svg.Skia |

  

### 2.2 Wymagania programowe

-  **System operacyjny:** Linux (np. Ubuntu 22.04+), Windows 10/11 lub macOS 12+.

-  **Środowisko:** .NET SDK **8.0** 

-  **Baza danych:** nie trzeba instalować osobno — SQLite tworzy plik przy pierwszym uruchomieniu.

-  **Opcjonalnie (boty LLM):** dostęp do API zgodnego z OpenAI (URL, klucz, model) — konfiguracja w aplikacji w zakładce „Presety OpenAI”. Może to prowadzić do dowolnego api http jakiegoś online providera, albo po prostu do naszego lokalnego api uruchomionego np. z llamacpp, które jest wystawione jako endpoint http.

  

### 2.3 Proces instalacji

  
1. Wypakuj repozytorium i przejdź do PokerApp

```bash

cd PokerApp

```

2. Instalacja pakietów NuGet:

```bash

dotnet restore

```

  

### 2.4 Proces konfiguracji

1.  **Zmienne środowiskowe (opcjonalne):**

-  `POKERAPP_DB` — ścieżka do pliku SQLite (domyślnie: `~/.local/share/PokerApp/poker_app.sqlite` na Linuxie).

2.  **Baza danych:** przy starcie wywoływane jest `PokerDbBootstrap.EnsureInitialized()` — tworzy schemat, jeśli nie istnieje. Osobne migracje EF nie są wymagane.

3.  **Presety OpenAI:** w menu → „Presety OpenAI” dodaj URL API, klucz i nazwę modelu.

4.  **Dane początkowe (seed):** uruchom tryb **„Seria turniejów (boty)”** z samymi **botami losowymi**. Bot losowy podejmuje decyzje natychmiastowo, więc serie kończą się szybko i sensownie wypełniają bazę rozdaniami - bez konieczności ręcznego skryptu seed. 

  

### 2.5 Uruchomienie w terminalu (developer)


```bash

dotnet  run

```

  

Otwiera się okno aplikacji desktopowej.

  

**Build release:**

  

```bash

dotnet  build  -c  Release

```

  

---

  

## 3. Uruchomienie projektu (użytkownik końcowy)

  

### 3.1 Linux — wersja bez instalowanego .NET

  

Z katalogu projektu (wymaga .NET SDK tylko u osoby budującej paczkę):

  

```bash

./scripts/publish-linux.sh

```

  

Powstanie katalog `dist/PokerApp-linux-x64/` z plikiem wykonywalnym `PokerApp` i zależnościami.
Normalny użytkownik uruchamia tylko:



  

```bash

./dist/PokerApp-linux-x64/PokerApp

```

  

**Wymagania sprzętowe:** Bardzo niskie, tak długo jak nie chcemy na tej samej maszynie uruchamiać lokalnego LLMa.

  

### 3.2 Windows / macOS


Po zainstalowaniu .NET 8 Runtime można uruchomić build opublikowany analogicznie (`dotnet publish -r win-x64` / `osx-x64 --self-contained true`). Gotowe paczki można umieścić w zakładce **Releases** na GitHubie.
Tego sam nie testowałem, działałem tylko na Linuxie.


  

---

  

## 4. Podręcznik użytkownika

  

### 4.1 Menu główne

  

-  **Gra** — konfiguracja stołu i start rozgrywki.

-  **Osobowości botów** — opisy stylu gry dla botów LLM.

-  **Presety OpenAI** — adres API, klucz, model.

-  **Powtórki** — lista zapisanych rozdań i serii turniejów.

-  **Wyjdź** — zamyka aplikację.

  

### 4.2 Ścieżka: gra z udziałem człowieka

  

1.  **Gra** → tryb „Gra”.

2. Ustaw buy-in, małą ciemę, liczbę botów (1–6), typ każdego bota (losowy / LLM), nazwy.

3. Dla bota LLM wybierz osobowość i preset OpenAI.

4.  **Start** — przejście do stołu.

5. Na swoją kolej: **Pas**, **Check/Call**, **Raise** (kwota w polu).

6. Po rozdaniu — ekran „Koniec rozdania”: opcjonalnie zapisz rękę (nazwa + checkbox).

7.  **Wyjdź do menu** — potwierdzenie „Tak” natychmiast wraca do menu.

  

### 4.3 Ścieżka: seria turniejów (boty)

  

1.  **Gra** → tryb **„Seria turniejów (boty)”**.

2. Podaj nazwę serii i liczbę turniejów w serii.

3. Skonfiguruj wyłącznie boty (bez gracza ludzkiego).

4. Po zakończeniu serii dane trafiają do bazy; statystyki widać w **Powtórki → Serie turniejów**.

  

**Seed danych:** ten tryb z samymi botami losowymi to zalecany sposób szybkiego generowania wielu gier i serii do testów i powtórek.

  

### 4.4 Powtórki

  

-  **Pojedyncze ręce** — rozdanía zapisane po grze z człowiekiem.

-  **Serie turniejów** — wybór serii, lista rozdań, statystyki (wygrane turniejów, średnia liczba rozdań, pul i czasu).

- Checkbox **„Ukryj ręce z pulą max poniżej …”** filtruje małe rozdania.

- Otwarcie powtórki: krok po kroku (**Dalej**). **Wszystkie karty graczy są widoczne od początku**, także gdy ktoś od razu spasował.

-  **Nie** pojawia się okno zapisu ręki po zakończeniu powtórki — wynik widać na pasku statusu stoła; powrót: **Wyjdź do menu** / **Wyjdź z gry**.

  

### 4.5 Role w systemie

  

Aplikacja jest jednoosobowa, bez logowania. Wszyscy użytkownicy mają ten sam zestaw funkcji (gra, konfiguracja botów, powtórki, eksport CSV serii), dlatego nie jest wymagana żadna kontrola uprawnień.

  

### 4.6 Przypadki brzegowe

  

- Pola liczbowe (buy-in, blindy, liczba turniejów) — niepoprawny tekst blokuje start z komunikatem błędu.

- Bot LLM bez presetu — komunikat przy starcie gry.

- Pusta nazwa zapisu ręki — zapis się nie wykona.

- Anulowanie wyjścia z gry — „Nie” zostawia w rozgrywce.

- Brak pliku bazy / zmiana wersji schematu — baza może zostać utworzona od nowa (patrz `poker_app.sqlite.ver`).

  

### 4.7 Przechowywane dane

  

- SQLite: zapisane ręce (`hand_history_json`), metadane (czas, max pula, nazwa), gracze przy stole, serie turniejów, statystyki serii, osobowości LLM, presety API.

- Pliki kart: `assets/svg-cards/`, tło menu: `assets/backgrounds/`.

- Dane API (klucze) — lokalnie w bazie; nie wysyłane poza wybrane endpointy przy grze botów LLM.

  

### 4.8 Najważniejszy mechanizm — powtórka rozdania

  

Historia to JSON zdarzeń (`start_hand`, `start_round`, `action`, `showdown`). Widok powtórki ustawia ilość żetonów graczy, karty i pulę według kolejnych akcji. Optymalizacja list powtórek: do listy ładowane są tylko metadane (bez całego JSON); pełna historia wczytywana jest przy **Otwórz**.



## 5. Plany rozbudowy

- Gra wieloosobowa przez sieć, co pozwoliłoby wielu graczom grać z wieloma botami przez internet lub po sieci lokalnej
- Zbudowanie agenta AI, ktory wykonuje własny tool calling, np. analizuje decyzje agentów AI w poprzednich rozdaniach/turniejach w celu podjęcia decyzji/przewidzenia jak zachowują się w określonych sytuacjach