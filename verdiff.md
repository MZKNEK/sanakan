# 1.4.10.17

Data: 2026-10-10

- Zmieniono karanie za rozpoznane scamowe obrazki — kara pojawia się już przy drugim takim obrazku w ciągu 2 minut zamiast przy trzecim.
- Zmieniono liczenie scamowych obrazków — każdy rozpoznany obrazek w wiadomości liczy się osobno, więc wiadomość z kilkoma takimi obrazkami może od razu skończyć się karą.
- Usunięto ostrzeżenie o scamie wysyłane razem z karą, gdy pierwsza wiadomość od razu kończy się mutem lub banem.

## Techniczne

- Przy mucie lub banie bot zapisuje w logach odciski obrazków z wiadomości, które nie pasowały do znanych scamów, co ułatwia dodawanie nowych.
