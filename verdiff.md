# 1.4.11.20

Data: 2026-11-11

- Naprawiono sortowanie kart na stronie po ataku, obronie i życiu, które teraz uwzględnia wartości widoczne na karcie.
- Naprawiono kolejność graczy z takim samym wynikiem w rankingach, która znów jest stała.
- Przyspieszono komendy portfel, subskrypcje, wyprawa status, ulubione, czego brak i jakie.
- Przyspieszono harem oraz oba profile karcianki.
- Przyspieszono rozpoczęcie wymiany.
- Zmieniono dodawanie kart do wymiany, które teraz sprawdza aktualny stan karty w chwili dodania.
- Przyspieszono wybór zwycięzcy polowania (safari).
- Przyspieszono panele gracza na stronie: limity, misje, przedmioty, talię, figurki, pakiety, listę życzeń i oznaczenia.
- Przyspieszono otwieranie i dodawanie pakietów przez stronę.

## Techniczne

- Bot pobiera z bazy tylko dane potrzebne danej komendzie, co zmniejsza obciążenie bazy.
- Powiadomienie o wolnym zapytaniu do API przychodzi teraz dopiero powyżej 12 sekund.
- Stara metoda dodawania pakietów dla strony nie zwraca już całej kolekcji gracza, tylko jego podstawowe dane i token.
