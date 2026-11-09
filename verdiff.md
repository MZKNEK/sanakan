# 1.4.11.13

Data: 2026-11-09

- Naprawiono błąd, przez który gracz opuszczający serwer czasem nie był oznaczany jako nieobecny i automatyczne usuwanie nieaktywnych kont go pomijało.

## Techniczne

- Zmieniono sprawdzanie, czy gracz jest jeszcze na którymś serwerze, tak że bot rzadziej odpytuje Discorda i nie zapycha już kolejki zapytań, gdy kilka osób wychodzi naraz.
