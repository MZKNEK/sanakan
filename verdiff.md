# 1.4.11.10

Data: 2026-11-08

- Zmieniono wygląd powiadomień o wyciszeniach i banach: widać rodzaj kary, powód, czas trwania, odliczanie do końca i kto ją nałożył, a ukarany dostaje podobną wiadomość prywatną.
- Naprawiono podwójną wiadomość po użyciu polecenia bana.
- Zmieniono listę wyciszonych: jest posortowana według końca kary, pokazuje odliczanie, oznacza kary automatyczne i wyświetla najwyżej 20 osób.
- Zmieniono wiadomość prywatną o ostrzeżeniu, która teraz podaje nazwę serwera i numer ostrzeżenia.
- Zmieniono wygląd zgłoszeń: mają link do zgłoszonej wiadomości, klikalny kanał i załączniki, a po rozpatrzeniu widać decyzję i kto ją podjął.
- Naprawiono godzinę w logu edytowanych wiadomości, który pokazywał czas napisania zamiast czasu edycji.
- Zmieniono wygląd logów usuniętych i edytowanych wiadomości, notatek „todo”, cytatów i logu kanałów głosowych.
- Zmieniono odpowiedzi poleceń konfiguracji moderacji, a przegląd konfiguracji jest po polsku i podpowiada pełne polecenia.
- Dodano odpowiedź, gdy bot nie znajdzie podanego użytkownika, kanału, roli lub wiadomości, zamiast milczenia.
- Zmieniono komunikaty o błędnie podanych argumentach polecenia na polskie, z nazwą błędnego argumentu.
- Zmieniono wygląd pomocy do poleceń: opis argumentów, przykład i aliasy w czytelnej formie.
- Dodano wyjaśnienie, co zrobić, gdy bot nie może wysłać wiadomości prywatnej, zamiast technicznego błędu.
- Naprawiono odmianę liczebników w wiadomościach gry, np. przy kartach, przedmiotach i czasie.
- Zmieniono wygląd wymiany: widać obie oferty, postęp i aktualny krok, a wymiana, która wygasła, mówi o tym wprost.
- Zmieniono pytania z reakcjami, które po wygaśnięciu szarzeją z dopiskiem „Pytanie wygasło”.
- Zmieniono wygląd loterii: ogłoszenie podaje liczbę kart do wygrania, a wyniki wyróżniają skalpele i karty ultimate i pokazują najcenniejsze karty na górze.
- Naprawiono podmianę oznaczeń, która pomijała część kart.
- Naprawiono informację o doświadczeniu z loterii, które pokazuje teraz, ile faktycznie trafiło do skrzyni.
- Naprawiono błąd, przez który podgląd listy obrazków liczył się do dziennej misji używania przedmiotów.
- Naprawiono komunikaty, które opisywały coś innego niż się wydarzyło, m.in. przy wyprawach, zmianie dere, doświadczeniu ze szkieletu i utracie karty.
- Zmieniono wygląd figurek: widać postęp, jakość, pasek doświadczenia, założone i brakujące części oraz potrzebne punkty konstrukcji.
- Zmieniono komunikaty przy częściach figurek, które wyjaśniają, dlaczego część nie pasuje, i ostrzegają o wyzerowaniu punktów konstrukcji.
- Zmieniono nazwy części figurek, które mają teraz w nazwie słowo „figurki”.
- Zmieniono wygląd widoku karty, profilu w grze, statystyk i listy aktywnych kart na czytelniejsze, podzielone na sekcje.
- Zmieniono polecenie z informacjami o bocie: pokazuje stan bota, wersję, czas działania, opóźnienie, prefiks serwera i przyciski z linkami, w tym do listy zmian.
- Zmieniono listę osób chcących kartę: nieaktywni gracze są w osobnej sekcji, a na jednej stronie mieści się więcej osób.
- Naprawiono długie listy kart, życzeń i osób chcących kartę, których koniec Discord potrafił nie wyświetlić, a przy okazji mieszczą więcej pozycji na stronę.
- Zmieniono profil i statystyki ze strony: w trakcie odnowienia bot wysyła ostatnio zapisany obraz z informacją, kiedy powstał.
- Zmieniono odnowienie statystyk ze strony z 30 do 5 minut.
- Dodano pokazywanie zapisanych statystyk ze strony, gdy Shinden nie odpowiada.
- Zmieniono komunikaty o odnowieniu poleceń, które pokazują odliczanie do ponownego użycia.
- Zmieniono odnawianie dziennego limitu pojedynków na północ i naprawiono błąd, przez który gra po północy blokowała pojedynki na ponad dobę.
- Zmieniono komunikat o wyczerpanym limicie pojedynków, który mówi, kiedy można grać dalej.
- Naprawiono komunikat o usunięciu koloru, który mówił, że kolor został kupiony.
- Zmieniono odpowiedzi przy zakupie koloru i emotek globalnych, które pokazują datę końca.
- Zmieniono odpowiedzi przy łączeniu konta z Shindenem, które wyjaśniają niezgodny nick i brak konta.

## Techniczne

- Dodano w logach bota wpisy o karach za obrazki, które łatwo znaleźć wyszukiwarką.
- Zmieniono komunikaty błędów, które zamiast technicznych szczegółów pokazują graczom zrozumiały tekst, a szczegóły trafiają do logów.
