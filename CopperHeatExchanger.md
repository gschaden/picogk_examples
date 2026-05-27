# prompt 1
Ich habe ein funktionierendes PicoGK-Beispielprojekt in VS Code geöffnet. Ich möchte, dass du mir eine neue C#-Komponente für einen additiv            gefertigten Plattenwärmetauscher (aus reinem Kupfer) mit einer Kühlleistung von genau 1 kW (1000 W) programmierst.                                                                                                                                                                                            Bitte gehe schrittweise vor:

  1. Analyse: Analysiere die bestehende Projektstruktur und schaue dir an, wie der PicoGK-Viewer und der Export im Beispielprojekt aufgerufen werden.  
  2. Thermodynamische Logik: Implementiere eine mathematische Berechnung im Code, die auf Basis der Wärmedurchgangsgleichung (Q_punkt = k * A *        
  delta_T_ln) die benötigte Oberfläche (A) ermittelt. Nutze für Kupfer eine Wärmeleitfähigkeit von 400 W/(m*K).
  3. Geometrie-Generierung (PicoGK): Setze die berechnete Oberfläche in eine kompakte, 3D-druckbare Platten- oder TPMS-Struktur (z.B. Gyroide) mittels 
   PicoGK-Voxels um. Achte darauf, dass die Struktur "self-supporting" ist (Winkel >= 45°), da im Inneren kein Support-Material gedruckt werden kann.  
  4. Integration: Erstelle eine Methode, die diesen Wärmetauscher generiert, im Viewer anzeigt und als Datei exportiert.

  BEVOR du mit dem Schreiben des finalen Codes beginnst:
  Bitte stelle mir gezielte Fragen zu den physikalischen und geometrischen Rahmenbedingungen, die du für die exakte Berechnung der Oberfläche (A) und  
  des Bauraums noch benötigst (z.B. Ein-/Austrittstemperaturen der Medien, Massenströme oder maximale Abmessungen der Bounding Box).

  Sobald wir diese Parameter geklärt haben, erstelle die entsprechenden C#-Dateien direkt im Projekt.

# prompt 2
ok ich brauch auch noch anschlüsse

# documentation

## Koordinatensystem

Der Ursprung liegt an der vorderen unteren linken Ecke des inneren Gyroid-Volumens.  
Alle Maße in Millimeter.

## Bounding Box (Innenvolumen)

| Achse | Von | Bis | Länge |
|-------|-----|-----|-------|
| X     | 0   | 50  | 50 mm |
| Y     | 0   | 50  | 50 mm |
| Z     | 0   | 30  | 30 mm |

Das Innenvolumen wird vollständig vom Gyroid-TPMS ausgefüllt (Zellgröße 10 mm, 5×5×3 = 75 Zellen).  
Gyroid-Wanddicke: 1 mm (2 × 0,5 mm Halbdicke, Cu-LPBF-konform).

## Außengehäuse (Shell)

Die 2 mm dicke Kupferschale umschließt das Innenvolumen allseitig:

| Achse | Von | Bis  | Länge |
|-------|-----|------|-------|
| X     | −2  | 52   | 54 mm |
| Y     | −2  | 52   | 54 mm |
| Z     | −2  | 32   | 34 mm |

## Anschlüsse (Ports)

Vier Rohrstutzen, je 11 mm Außendurchmesser / 7 mm Innendurchmesser (4 mm Wanddicke).  
Stutzenüberstand ab Außenschalenoberfläche: **12 mm**.  
Gesamtstützenlänge (inkl. Schalendurchdringung 2 mm): **14 mm**.

**Gegenstrom-Layout:** Heißmedium auf X-Flächen, Kaltmedium auf Y-Flächen.

| Port   | Fluid | Fläche       | Stutzenspitze (Anschluss) | Mittelpunkt (Y, Z) |
|--------|-------|--------------|---------------------------|--------------------|
| H_IN   | Heiß  | X = 0 mm     | X = −14 mm                | Y = 25, Z = 15     |
| H_OUT  | Heiß  | X = 50 mm    | X = +64 mm                | Y = 25, Z = 15     |
| C_IN   | Kalt  | Y = 50 mm    | Y = +64 mm                | X = 25, Z = 15     |
| C_OUT  | Kalt  | Y = 0 mm     | Y = −14 mm                | X = 25, Z = 15     |

```
Draufsicht (Z-Richtung, von oben):

              C_IN  (Y+)
                |
    H_IN ──[Gyroid 50×50]── H_OUT
                |
              C_OUT (Y−)
```

**Gesamtabmessungen mit Stutzen:**

| Richtung | Gesamt  | Erklärung                                |
|----------|---------|------------------------------------------|
| X        | 78 mm   | 14 (H_IN) + 54 (Gehäuse) + 14 (H_OUT)   |
| Y        | 78 mm   | 14 (C_OUT) + 54 (Gehäuse) + 14 (C_IN)   |
| Z        | 34 mm   | nur Gehäuse, keine Stutzen auf Z-Flächen |