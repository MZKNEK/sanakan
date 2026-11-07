# 1.4.11.5

Data: 2026-11-07

- Naprawiono rzadki błąd, przez który po zmianie danych gracza, kart lub ustawień serwera bot mógł jeszcze długo pokazywać ich stary stan.

## Techniczne

- Zmieniono sposób przechowywania często używanych danych w pamięci bota, aby nie zależał od zewnętrznego dodatku, co ułatwi przejście na nowszą wersję platformy.
- Dodano godzinne i dobowe raporty o tym, jak często bot korzysta z zapamiętanych danych, ile kosztuje ich ponowne wczytanie i ile miejsca zajmują w pamięci.
- Dodano możliwość uruchomienia bota na bazie danych zapisanej w jednym pliku, bez osobnego serwera bazy danych, z takim samym wyszukiwaniem i sortowaniem tekstu jak dotychczas.
