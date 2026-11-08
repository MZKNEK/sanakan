# 1.4.11.8

Data: 2026-11-08

- Zmieniono wygląd obrazka awansu na poziom: jest mniejszy, pasuje do wyglądu Discorda, a przy nieruchomym awatarze strzałka awansu podskakuje.
- Przyspieszono wczytywanie profilu na stronie, zwłaszcza u graczy z dużą liczbą kart.
- Przyspieszono listy kart ultimate i kart unikatowych na stronie.
- Przyspieszono polecenie pokazujące statystyki ze strony (strona, ile, otaku).
- Przyspieszono polecenie wypisujące oznaczenia u graczy, którzy mają ich dużo.
- Naprawiono błąd, przez który obrazek karty mógł zostać zapisany bez postaci, gdy strona z obrazkami postaci chwilowo nie odpowiadała.
- Naprawiono błąd, przez który obrazek karty do profilu mógł się nigdy nie wygenerować.

## Techniczne

- Przyspieszono przyznawanie pakietów za aktywność, dobieranie przeciwników do pojedynków, liczenie chętnych na karty i rozdawanie kart w loteriach, które wczytują teraz z bazy tylko potrzebne dane.
- Dodano zapis w logach, gdy nie uda się pobrać obrazka postaci na kartę.
