# 1.4.10.30

Data: 2026-10-30

- Naprawiono błąd, przez który przy przeglądaniu kart gracza na stronie część kart mogła się powtarzać na kolejnych stronach albo nie pojawić się wcale.
- Przyspieszono otwieranie i odbieranie pakietów przez stronę u graczy z dużą kolekcją kart.

## Techniczne

- Naprawiono możliwość zapchania pamięci bota przez masowe wyszukiwanie kart ultimate i unikatowych na stronie.
- Przyspieszono obsługę zapytań ze strony dzięki mniejszemu blokowaniu bota przy pracy z bazą danych.
- Dodano zapisywanie w logach nieoczekiwanych błędów w komunikacji ze stroną, a strona dostaje wtedy czytelną odpowiedź.
- Dodano ostrzeżenie przy starcie bota o zbyt krótkich kluczach aplikacji.
- Zmieniono sposób uruchamiania serwera dla strony, dzięki czemu problemy przy starcie są zgłaszane od razu.
