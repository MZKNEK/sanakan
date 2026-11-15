# 1.5.0.7

Data: 2026-11-15

- Naprawiono błąd, przez który bardzo duża animacja ustawiona w profilu wyłączała bota przy każdym wyświetleniu profilu.
- Zmieniono przyjmowanie animacji do profilu: zbyt duże są odrzucane z wyjaśnieniem, a edytor profilu na stronie sam je zmniejsza przed wysłaniem.
- Zmieniono wyświetlanie profilu: uszkodzony obrazek jest pomijany, zamiast blokować cały profil.
- Usunięto profil w podwójnej rozdzielczości: wszystkie profile mają znów zwykły rozmiar, a gracze, którzy go kupili, dostali zwrot TC.

## Techniczne

- Bot pilnuje pamięci zużywanej na rysowanie obrazków, dzięki czemu nie wyłącza się przy bardzo dużych plikach.
- Bot nie wraca już do poprzedniej wersji po awarii, za którą nowa wersja nie odpowiada.
