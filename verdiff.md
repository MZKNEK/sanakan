# 1.4.10.27

Data: 2026-10-29

- Dodano komunikację z Shindenem, dzięki której nowe wpisy z wybranych kanałów administracyjnych trafiają automatycznie na stronę.
- Dodano możliwość skasowania dowolnej wiadomości od bota na prywatnej rozmowie przez dodanie do niej reakcji 🗑️.

## Techniczne

- Dodano polecenia dla deweloperów do zmiany klucza dostępu do Shindena i danych logowania do bazy danych, przyjmowane wyłącznie w prywatnej rozmowie z botem.
- Naprawiono błąd, przez który tajny klucz wpisany w poleceniu mógł po skasowaniu wiadomości trafić na kanał logów serwera.
