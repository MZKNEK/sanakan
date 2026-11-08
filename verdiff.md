# 1.4.11.9

Data: 2026-11-08

- Przyspieszono tworzenie obrazków kart, bo obrazek postaci jest teraz pobierany raz zamiast dwa razy.

## Techniczne

- Zmieniono zachowanie bota, gdy strona z obrazkami postaci ogranicza liczbę zapytań: bot od razu przestaje ponawiać próby, żeby nie przedłużać blokady, i nie zgłasza tego jako błędu na kanale z logami.
- Dodano w logach przyczynę nieudanego pobrania obrazka postaci.
