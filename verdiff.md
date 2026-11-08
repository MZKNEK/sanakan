# 1.4.11.11

Data: 2026-11-09

- Zmieniono wygląd widoku karty, skróconego widoku karty i profilu karcianki na przestronniejszy, z informacjami w kolumnach i osobnych wierszach.
- Zmieniono widok figurki: części są wypisane jedna pod drugą, a budowana część ma własny pasek postępu.
- Dodano ukryte polecenia pokazujące kartę, skrócony widok karty i profil karcianki w poprzednim wyglądzie ... prosił @mvysther
- Zmieniono walki z kartami ultimate: każde trafienie zadaje co najmniej 15% ataku, więc karta z bardzo wysoką obroną nie jest już praktycznie nieśmiertelna ... prosił @medi_
- Zmieniono liczenie mocy kart i talii, która teraz dużo lepiej oddaje faktyczną siłę w walce, zwłaszcza kart ultimate ... prosił @medi_
- Zmieniono dobór przeciwników w pojedynkach, który teraz szuka talii o podobnej mocy, a nie tylko podobnego rankingu ... prosił @jarmar
- Zmieniono nagrody PC za pojedynki, które zależą teraz od siły pokonanego rywala, a nie od własnej rangi ... prosił @jarmar
- Zmieniono progi rang sezonowych tak, żeby w ciągu miesiąca dało się zdobyć wszystkie, łącznie z Konsulem ... prosił @jarmar
- Zmieniono pojedynki tak, że gracz, który został wyzwany, zyskuje lub traci tylko połowę punktów rangi ... prosił @jarmar
- Zmieniono limity mocy talii w pojedynkach: dolny wynosi teraz 25, a górnego praktycznie nie ma, bo uczciwość zapewnia dobór przeciwników po mocy ... prosił @jarmar
- Naprawiono pigułki, które można było dawać karcie mimo osiągnięcia maksimum danej statystyki, przez co przepadały bez efektu ... prosił @jarmar

## Techniczne

- Dodano polecenie dla deweloperów do szybkiego przeliczenia mocy wszystkich kart i talii bez dużego obciążenia bazy.
- Dodano polecenia dla deweloperów do wyszukiwania i usuwania z bazy osób, których nie ma już na żadnym serwerze, a których nie obejmuje codzienne czyszczenie.
