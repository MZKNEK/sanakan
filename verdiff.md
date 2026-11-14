# 1.5.0.0

Data: 2026-11-14

- Przyspieszono pokazywanie kart po ich zmianie, bo obrazek karty rysuje się teraz sam chwilę po ulepszeniu czy użyciu przedmiotu.
- Przyspieszono przygotowywanie obrazków kart, zwłaszcza animowanych, których zapis trwa teraz ponad dwa razy krócej.
- Naprawiono błąd, przez który przy chwilowym problemie z pobraniem obrazka postaci karta mogła na stałe zostać bez obrazka; teraz rysuje się ponownie, gdy obrazek znów jest dostępny.
- Przyspieszono wyszukiwanie po tekście na liście wszystkich kart posortowanej po rzadkości.
- Naprawiono błąd, przez który powtórzenie otwarcia pakietu w aplikacji po zerwanym połączeniu mogło otworzyć kolejny pakiet.
- Naprawiono błąd, przez który powtórzenie zmiany aktywności karty w aplikacji cofało tę zmianę.
- Naprawiono błąd, przez który powtórzone dodanie pakietów przez stronę mogło je dodać dwa razy.
- Zmieniono ustawianie ulubionej postaci przez aplikację, które przy tej samej postaci nie zgłasza już błędu.

## Techniczne

- Przeniesiono bota na nowszą wersję platformy, na której działa, oraz na nowsze biblioteki, co poprawia wydajność i bezpieczeństwo.
- Zmieniono sposób uruchamiania i aktualizacji bota na serwerze: każda aktualizacja buduje się osobno, a nieudana wraca sama do poprzedniej wersji; pierwsze przejście trzeba zrobić ręcznie według instrukcji projektu.
- Dodano sprawdzanie serwera przed instalacją, które wskazuje, czego brakuje do uruchomienia i aktualizacji bota.
- Zwiększono bezpieczeństwo połączenia ze stroną: operacje na pakietach i punktach TC wymagają identyfikatora operacji, a powtórzone żądanie nie wykona się drugi raz.
- Zmieniono część odpowiedzi dla strony, które teraz dokładniej opisują powód odmowy i podają, po jakim czasie spróbować ponownie; strona musi być na to gotowa przed wdrożeniem tej wersji.
- Dodano dokładniejsze logi: godzinne podsumowanie rysowania kart, rozróżnianie anonimowych odwołań do bota i zapis zmian kluczy aplikacji graczy.
- Dodano samoczynne aktualizowanie budowy bazy danych przy uruchomieniu bota, więc zmiany w bazie nie wymagają już ręcznych poprawek.
- Dodano polecenie dla administracji, które liczy, ile miejsca zajmują obrazki pozostałe po usuniętych kartach i graczach, niczego przy tym nie usuwając.
- Usunięto miniatury kart, z których nie korzystała żadna strona; pod ich dawnym adresem jest teraz pełna karta, a na serwerze zwalnia się miejsce.
- Zmieniono zapis obrazków, które zajmują teraz mniej miejsca, a animowane karty ok. 13% mniej.
- Usunięto z kanału logów osobne powiadomienia o wolnych odpowiedziach dla strony i aplikacji; nadal liczy je godzinne podsumowanie.
- Zmieniono zapis ustawień bota, które bot zapisuje teraz czytelnie, po jednym polu w linii.
