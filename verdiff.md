# 1.4.10.16

Data: 2026-10-09

- Naprawiono błąd, przez który wyciszeni gracze mogli nie zostać odciszeni po upływie kary.
- Naprawiono błąd, przez który bany czasowe mogły nie wygasać.
- Naprawiono błąd, przez który bot próbował wyciszać zbanowanych graczy.
- Naprawiono błąd, przez który kolory i globalne emotki mogły zostawać po wygaśnięciu subskrypcji.
- Naprawiono automatyczne banowanie przy rajdach, które mogło pomijać część kont.
- Naprawiono wyświetlanie profilu, gdy Shinden nie odpowiada — profil pokazuje się bez danych ze strony zamiast nie pokazywać się wcale.
- Naprawiono komendę łączenia konta z Shindenem, która przy braku odpowiedzi strony nie odpowiadała wcale.
- Naprawiono sprawdzanie gracza przez moderację, które przy braku odpowiedzi Shindena nie wyświetlało raportu.

## Techniczne

- Bot nie zapycha już logów powtarzającymi się błędami przy graczach, którzy opuścili serwer.
- Profil gracza bez połączonego konta nie wysyła już zbędnych zapytań do Shindena.
- Strona otrzymuje czytelny komunikat zamiast ogólnego błędu, gdy Shinden nie odpowiada, a listy kart nie zawieszają się przy jego awarii.
