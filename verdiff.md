# 1.4.11.4

Data: 2026-11-07

- Dodano przycisk z profilem gracza na Alterze w statystykach ze strony i zmieniono nazwę przycisku profilu na „Shinden”.
- Zmieniono rozpoznawanie bieżącego sezonu anime w pakietach sezonowych, aby korzystało z informacji o sezonie podawanej przez Shindena.
- Naprawiono otwieranie pakietów sezonowych, które mogło się nie udać, gdy wylosowana postać została usunięta z Shindena.
- Zmieniono komunikat loterii, gdy nie uda się pobrać sezonu i zamiast pakietu sezonowego wpada zwykły pakiet.
- Naprawiono błąd, przez który gracze z czarnej listy mogli dalej korzystać z funkcji bota przez stronę.
- Zmieniono ustawianie ulubionej postaci przez stronę, aby tak jak w bocie nie było możliwe w trakcie wymiany.
- Naprawiono profil na stronie, który zaraz po połączeniu kont mógł jeszcze przez kilkanaście minut pokazywać brak połączenia.
- Naprawiono pakiety kart przyznane przez stronę z błędnymi ustawieniami jakości, których nie dało się otworzyć.
- Dodano informację o sezonie przy pakietach sezonowych na liście pakietów udostępnianej stronom i aplikacjom.

## Techniczne

- Odebranie stronie lub aplikacji dostępu działa teraz od razu, a nie dopiero po wygaśnięciu jej logowania.
- Otwieranie pakietów zleconych przez stronę nie blokuje już kolejki poleceń bota, gdy Shinden odpowiada wolno.
- Strony nie mogą już zlecić pakietów z niepoprawnymi ustawieniami ani zbyt wielu pakietów naraz.
- Zmiany ustawień połączenia z Shindenem działają od razu, bez restartu bota.
- Dodano możliwość pobrania i uzupełnienia danych wskazanego sezonu anime przez twórców bota.
