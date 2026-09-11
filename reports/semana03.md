# Entrega P1A3 — Semana 03: Análisis sísmico estático, superposición y diseño de secciones

**Equipo 4** · Curso de Estructuras de Hormigón Armado · **Fecha: 11 de septiembre de 2026**

Modelo elástico 3D en OpenSeesPy (`ndm=3, ndf=6`, diafragmas rígidos, base empotrada) del edificio descrito en `DOCUMENTO_EDIFICIO.md`. Esta entrega cierra el **péptico de análisis**: casos base gravitatorios y sísmicos, validación del camino de cargas, sismo pseudo-estático, superposición de combinaciones, y las primeras piezas de **diseño de secciones** (momento-curvatura, curvas P-M, verificación RC y demanda-capacidad).

---

## 1. Casos base (G, Q, EX, EY)

### 1.1 Definición de los casos

| Caso | Contenido | Reparto sobre vigas | Carga superficial |
|---|---|---|---|
| **G** (carga muerta) | Peso propio losa (e=0,15 m) + peso muerto adicional | `w = q · A_trib / L` (mismas áreas de `vigas_tributarias.json`) | 6,225 kN/m² (3,675 + 2,550) |
| **Q** (carga viva) | Sobrecarga de uso (400 kg/m²) | idéntico al de G (se **reutilizan** las áreas tributarias) | 3,920 kN/m² |
| **EX** (sismo ±X) | Fuerzas estáticas horizontales pseudo-estáticas | fuerzas aplicadas en el **nodo maestro** de cada losa | — (ver §3) |
| **EY** (sismo ±Y) | Fuerzas estáticas horizontales pseudo-estáticas | fuerzas aplicadas en el nodo maestro | — (ver §3) |

Las fuerzas sísmicas se aplican como fuerzas nodales en el **diafragma** de cada nivel (un solo punto de control por losa, `9500+k`), de modo que el reparto de cortante entre columnas y muros resulta del equilibrio del modelo y no de un supuesto previo.

Voladizos: el modelo incluye las losas en voladizo definidas en `definir_voladizos()` (nivel 3 en dirección X, bordes ancho 4,10 m y 0,80 m; nivel 5 en dirección Y, ancho 5,85 m), cargadas con la misma `q` a lo largo de la viga de borde.

### 1.2 Resultados globales y máximos

Tablas resumen por caso (máximos sobre **todos los elementos y niveles**, extraídos de `reports/salidas/casos_base_*.json`):

| Caso | ΣRz [kN] | Columna más cargada | Carga axial máx. | Viga de mayor momento |
|---|---|---|---|---|
| G | 24 608,3 | columna eje F-2 (11020) · nivel 1 | P = 2 497,2 kN | viga X L=10 m · nivel 3 (20313): My = −341,4 kN·m |
| Q | 15 496,3 | columna eje F-2 (11020) · nivel 1 | P = 1 572,5 kN | viga X L=10 m · nivel 3 (20313): My = −215,0 kN·m |

| Caso | ΣR_cortante [kN] | Elemento más solicitado por flexión | Valor |
|---|---|---|---|
| EX | ΣR_X = −11 824,3 | viga X L=5,0 m · nivel 2 (20204) | My = 1 714,2 kN·m |
| EX | | muro/núcleo (X=4250, Y=3576) · nivel 1 (11033) | My = 800,3 kN·m |
| EY | ΣR_Y = −11 824,3 | viga Y L=7,25 m · nivel 2 (30200) | My = 2 046,7 kN·m |
| EY | | muro/núcleo (X=4250, Y=3576) · nivel 1 (11033) | My = −99,4 kN·m |

La columna más cargada bajo G+Q (elemento 11020, eje F-2, 1° subterráneo) es la que se tomará como sección representativa en las secciones 5–9.

---

## 2. Carga viva (reutilización de áreas tributarias y conservación)

### 2.1 Reuso del sistema tributario

El caso Q usa exactamente las mismas áreas tributarias que G (`vigas_tributarias.json`): el camino de cargas es independiente de la carga, por lo que **un solo cálculo geométrico** genera `w_i` para todas las cargas superficiales. La carga de viga en cada elemento es `w = q · A_trib / L`, reproducida por el script para G y Q programáticamente (loop sobre el mismo `area_tributaria`).

### 2.2 Verificación de conservación

La carga total aplicada al modelo debe igualar la suma de las reacciones verticales de la base, **caso por caso y piso por piso** (se controló con la suma `Vz_i + Vz_j` de los extremos de cada una de las 27 vigas por nivel):

**Caso Q**

| Nivel | Σ(w·L) modelo [kN] | q·A_piso [kN] | Voladizos [kN] | Diferencia [kN] |
|---|---|---|---|---|
| 1 | 2 852,31 | 2 848,86 | — | 3,45 |
| 2 | 2 852,31 | 2 848,86 | — | 3,45 |
| 3 | 3 716,68 | 2 848,86 | 864,36 | 3,46 |
| 4 | 2 852,31 | 2 848,86 | — | 3,45 |
| 5 | 3 222,66 | 2 848,86 | 370,35 | 3,45 |
| **Σ** | **15 496,28** | | | = **ΣRz (base) = 15 496,28 kN** |

**Caso G**

| Nivel | Σ(w·L) modelo [kN] | q·A_piso [kN] | Voladizos [kN] | Diferencia [kN] |
|---|---|---|---|---|
| 1 | 4 529,50 | 4 524,02 | — | 5,48 |
| 2 | 4 529,50 | 4 524,02 | — | 5,48 |
| 3 | 5 902,13 | 4 524,02 | 1 372,61 | 5,50 |
| 4 | 4 529,50 | 4 524,02 | — | 5,48 |
| 5 | 5 117,62 | 4 524,02 | 588,12 | 5,48 |
| **Σ** | **24 608,25** | | | = **ΣRz (base) = 24 608,25 kN** |

**En todo el modelo**: `Σ(w·L) = ΣRz de la base` al criterio de punto flotante (diferencias < 6 kN frente a ~24 600 kN, < 0,03 %, atribuibles al redondeo de las áreas tributarias). Además, la suma `G + Q` reconstruye la carga nominal del documento del edificio:

`qG (G+Q) = 10,145 kN/m² ≈ qG nominal = 10,15 kN/m²` ✓ (el Q propagado por el modelo es exactamente 3,92 kN/m² y el `qG` de catálogo incluía la sobrecarga, redondeada a 10,15).

> **Hallazgo para revisar**: los muros (33 por nivel) presentan `P ≈ 0,0 kN` porque el camino gravitatorio del modelo no apoya vigas sobre muros (se cargan las vigas; los muros sólo reciben el diafragma). Es coherente con el `DOCUMENTO_EDIFICIO.md` (P muro = 0) pero debe corregirse en el modelo si se quiere el efecto de las cargas gravitatorias sobre las paredes del núcleo.

---

## 3. Sismo pseudo-estático

### 3.1 Parámetros adoptados

| Parámetro | Valor | Justificación / fuente |
|---|---|---|
| Zona sísmica / aceleración | A0 = 0,40 g | Zona 3 (normativa chilena) |
| Tipo de suelo | S = 1,05 | Suelo tipo II |
| Importancia | I = 1,00 | Edificio de oficinas/uso normal |
| Factor de reducción | R = 5,5 | Sistema sismorresistente de muros |
| Coeficiente C calculado | C_calc = 0,21 | Fórmula básica (fund. en período corto) |
| **C adoptado** | **0,25** | Conservador: representa el meseta de período corto del espectro (ver discusión en §10) |
| ψ (fracción de carga viva) | 0,25 | Combinación sísmica de masa |

### 3.2 Pesos, cortante basal y fuerzas por nivel

`Wi = A_piso · (G + ψ·Q) + peso propio estructural`

| Nivel | h [m] | Wi [kN] | Wi·hi [kN·m] | Fi [kN] | Corte acumulado [kN] |
|---|---|---|---|---|---|
| 5 | 19,80 | 9 459,4 | 187 296 | 3 941,4 | 3 941,4 |
| 4 | 15,84 | 9 459,4 | 149 837 | 3 153,1 | 7 094,5 |
| 3 | 11,88 | 9 459,4 | 112 378 | 2 364,9 | 9 459,4 |
| 2 | 7,92 | 9 459,4 | 74 918 | 1 576,6 | 11 036,0 |
| 1 | 3,96 | 9 459,4 | 37 459 | 788,3 | 11 824,3 |
| **Σ** | | **47 297,1** | 561 888 | **V0 = 11 824,3** | |

- Detalle del peso por piso: 4 223,2 kN de estructura + 726,75 m² × (6,225 + 0,25×3,92) = 5 236,2 kN de losa → **W_piso = 9 459,4 kN**; **W_total = 47 297,1 kN**.
- `V0 = C · ΣW = 0,25 × 47 297,1 = 11 824,3 kN`.

Las fuerzas `Fi` se aplican como fuerzas nodales horizontales en el nodo maestro de cada piso (diafragma). La **conservación** del cortante queda verificada al obtener `ΣR_X = ΣR_Y = 11 824,3 kN` en la base para EX y EY respectivamente (igual a V0).

### 3.3 Desplazamientos y derivas elásticas (nodo maestro)

**Caso EX** (u en X = dirección de la fuerza)

| Nivel | ux [m] | Drift (entre niveles) |
|---|---|---|
| 5 | 0,0559 | 0,0020 |
| 4 | 0,0480 | 0,0029 |
| 3 | 0,0367 | 0,0036 |
| 2 | 0,0226 | 0,0036 |
| 1 | 0,0082 | 0,0021 |

**Caso EY** (u en Y)

| Nivel | uy [m] | Drift (entre niveles) |
|---|---|---|
| 5 | 0,0686 | 0,0020 |
| 4 | 0,0607 | 0,0033 |
| 3 | 0,0477 | 0,0043 |
| 2 | 0,0307 | 0,0047 |
| 1 | 0,0122 | 0,0031 |

La estructura se desplaza más en Y (núcleo con menos rigidez en esa dirección); la deriva de entrepiso máxima (EY, nivel 2) es **0,0047**, dentro del rango típico elástico para análisis preliminar (la rotación de los diafragmas `rz` es pequeña, ≤ 0,0004 rad).

---

## 4. Superposición (3 o más combinaciones)

### 4.1 Combinaciones aplicadas

Se definieron combinaciones con **tres o más casos** (se precisó 3+ combinaciones):

| Combo | Expresión | Nº casos | característica |
|---|---|---|---|
| U1 | 1,4·G | 1 | gravitatorio al máximo |
| U2 | 1,2·G + 1,6·Q | 2 | gravitatorio de servicio |
| U3 | **1,2·G + 1,0·Q + 1,0·EX** | 3 | sísmico +X (3 términos) |
| U4 | **0,9·G + 1,0·EY** | 2 | sísmico −Y con mínimo G |

### 4.2 Validación de la linealidad (superposición vs corrida directa)

Se completó cada combinación de dos maneras independientes:

1. **Superposición algebraica** de los vectores de esfuerzos de los casos base (`M = Σ αi·Mi`), y
2. **Corrida directa** del modelo con las cargas combinadas.

Para los tres elementos gobernantes (columna F-2, viga de mayor momento y muro del núcleo) en las 4 combinaciones y las componentes P–My–Mz, la diferencia resultados es de orden **1e‑13/1e‑14 absoluto** (es decir, **0,0000 %**): el modelo es **lineal elástico** y la superposición es válida. Tabla extractada de `reports/salidas/combinaciones.json`:

| Combo | Elemento | Componente | Superpuesto | Directo | dif. [%] |
|---|---|---|---|---|---|
| U2 | col. 11020 | P | 5 512,71 | 5 512,71 | 0,0 |
| U2 | viga 20313 | My | −753,59 | −753,59 | 0,0 |
| U3 | col. 11020 | P | 4 609,87 | 4 609,87 | 0,0 |
| U3 | viga 20204 | My | 1 684,09 | 1 684,09 | 0,0 |
| U3 | muro 11033 | My | 800,69 | 800,69 | 0,0 |
| U4 | col. 11021 | Mz | 1 722,54 | 1 722,54 | 0,0 |

Este chequeo habilita: (i) evaluar cualquier combinación sin re-correr el modelo, (ii) usarlo como test de regresión del script, y (iii) disponer de vectores de demanda listos para diseño.

---

## 5. Momento-Curvatura (sección representativa de columna)

### 5.1 Sección

**COL_70** (0,70 m × 0,70 m), Ag = 0,49 m², armadura longitudinal **12φ25** (4+4+4), recubrimiento al eje de barra de 40 mm → **Ast = 5 890 mm², ρ = 1,20 %**. Materiales: hormigón G35 (`f'c = 35 MPa`, ε0 = 0,002, εcu = 0,003, diagrama parábola–rectángulo tipo Hognestad) y acero A63‑42H (`fy = 420 MPa`, Es = 200 GPa, εy = 0,0021, endurecimiento al 0,5% Es desde εsh = 0,01 a εsu = 0,09).

### 5.2 Método

Análisis de sección por **fibras de hormigón + barras discretas** (equivalente al `section Fiber` de OpenSees) implementado en Python puro (`scripts/momento_curvatura.py`): se impone la **compatibilidad de deformaciones** `ε(z) = φ·(c − z)`, se resuelve el eje neutro `c` para que la fuerza axial iguale la carga impuesta `P = const.`, y se integra el momento. Se usaron 64 fibras de hormigón.

**Carga axial representativa**: `P = 5 513 kN`, la de la columna más cargada (F‑2) con `1,2G + 1,6Q` (ver §9). Se curvea también para P = 0 y 2 500 kN para ver el efecto de la axial.

### 5.3 Curvas M-φ y resultados

| P [kN] | φ_y [1/m] | M_y [kN·m] | φ_u [1/m] | M_u [kN·m] | Causa del término |
|---|---|---|---|---|---|
| 0 | 0,0041 | 602,5 | 0,0456 | 804,7 | aplast. hormigón |
| 2 500 | 0,0052 | 1 196,9 | 0,0167 | 1 426,5 | aplast. hormigón |
| 5 513 | 0,0059 | 1 680,8 | 0,00980 | **1 773,3** | aplast. hormigón |

**Criterio de término (documentado)**: `φ_u = min φ { ε_c (fibra extrema comprimida) ≥ εcu = 0,003 }` (aplastamiento) **o** `{ ε_s (barra más traccionada) ≤ −εsu = 0,09 }` (fractura del acero). En todos los casos con la axial de diseño termina **por aplastamiento del hormigón**, lo que es esperable para una columna con ρ = 1,2 % y carga axial moderada: la ductilidad de curvatura aumenta al bajar la axial (φ_u = 0,0456 para P=0 vs 0,0098 para P=5 513 kN).

**Rigidez inicial**: `EI0` de la curva (pendiente del primer tramo) comparada con la sección bruta:

| P [kN] | EI0 [kN·m²] | EIgg (Ec·Ig) [kN·m²] | EI0/EIgg | EI0/(E0·Ig) |
|---|---|---|---|---|
| 0 | 149 700 | 556 324 | 0,27 | 0,21 |
| 2 500 | 726 200 | 556 324 | 1,31 | 1,04 |
| 5 513 | 714 300 | 556 324 | 1,28 | 1,02 |

> **Nota de consistencia material**: el módulo de la parábola en el origen es `E0 = 2·f'c/ε0 = 35 000 MPa`, mientras que el modelo estructural global usa `Ec = 4 700·√f'c = 27 806 MPa`. La relación EI0/EIgg > 1 en los casos con axial (y ≈ 1 frente a E0·Ig) se explica por esta diferencia: para diseño es una discrepancia aceptable, pero debe unificarse el módulo si se quiere coincidencia numérica exacta entre el análisis elástico y el de secciones.

**Sensibilidad de discretización** (nº de fibras de hormigón, P = 5 513 kN):

| Fibras | M_u [kN·m] | φ_u [1/m] | ΔM_u vs 80 fibras |
|---|---|---|---|
| 10 | 1 776,4 | 0,00971 | +0,18 % |
| 20 | 1 772,1 | 0,00980 | −0,06 % |
| 40 | 1 773,1 | 0,00980 | −0,01 % |
| 80 | 1 773,2 | 0,00980 | 0,00 % |

Convergencia excelente (< 0,2 %), por lo que 40–64 fibras es suficiente. Figura: `reports/fig/mf_curva.png`.

---

## 6. Curva P-M de la columna (12φ25)

Se genera la **envolvente de interacción P-M** variando el eje neutro `c` con compatibilidad de deformaciones y el mismo par de material (§5.1). Puntos característicos de la columna COL_70:

| Punto | c [mm] | P [kN] | M [kN·m] |
|---|---|---|---|
| Compresión pura (Pn0) | — | 16 876 | 0 |
| Descompresión (c = H, ε_inf = 0) | 700 | 14 392 | 1 007 |
| Balanceado | 388 | 7 274 | 1 865 |
| Flexión pura (P = 0) | ~65 | ≈ 0 | **804** |
| Tracción pura | — | −2 474 | 0 |

La envolvente es convexa y el punto balanceado (P ≈ 7 274 kN) está por encima de las axiales de diseño reales (≈5 500 kN), por lo que el diseño de la columna estará controlado más por la combinación gravitatoria que por el momento sísmico. Figura: `reports/fig/pm_columna.png`.

---

## 7. Curva P-M del muro (MUR_20)

**MUR_20** del modelo: espesor 0,20 m × longitud equivalente **1,0 m** (pier unitario) con armadura vertical de 2 mallas φ12@200 (10φ12 → Ast = 1 131 mm², **ρ_l = 0,565 %**). Curva de interacción (dirección fuerte, d = L = 1,0 m):

| Punto | P [kN] | M [kN·m] |
|---|---|---|
| Compresión pura (0,85·f'c·Ag + fy·Ast) | 6 391 | 0 |
| Descompresión (c = H, ε_inf = 0) | 5 582 | 494 |
| Balanceado | 2 813 | 879 |
| Flexión pura (P = 0) | ≈ 0 | **224** |

> **Decisión de modelo**: se evaluó también una línea continua con L=9,42 m (muros 33/34/35 colineales), que arroja capacidades de orden 60 000 kN / 20 000 kN·m. Esos valores son aritméticamente correctos pero **no se adoptaron**: mezclan la demanda del pier (800,7 kN·m, elemento 11033 del modelo elástico) con la capacidad de toda la línea, y el modelo representa el núcleo con piers sueltos de 1,0 m separados ~4,7 m. La DCR de §9 se hace, por lo tanto, sobre el **pier del modelo**; corregirla exige re-modelar el muro con su longitud real en el propio modelo elástico (ver §10). Figura: `reports/fig/pm_muro.png`.

---

## 8. Verificación RC de las secciones

Verificación con las fórmulas de compresión/control de ACI 318 (valores **nominales**, sin φ):

| Magnitud | Columna COL_70 | Muro MUR_20 |
|---|---|---|
| **Pn0 = 0,85·f'c·(Ag − Ast) + fy·Ast** [kN] | **16 876** | **6 391** |
| Límite 0,8·Pn0 [kN] | 13 501 | 5 113 |
| Punto balanceado (Pb, Mb) | (7 274 kN; 1 865 kN·m) | (2 813 kN; 879 kN·m) |
| Flexión pura Mn [kN·m] | 804 | 224 |
| ρ [%] | 1,20 | 0,565 |

Chequeo ACI de compresión pura: el término de la "meseta" 0,85·f'c actúa sobre (Ag−Ast) y el acero se lleva su fy; el punto balanceado se calcula con `c_b = εcu·d/(εcu + εy)`. Los tres valores característicos (Pn0, balanceado, flexión pura) quedan sobre la curva P-M, cerrándose la verificación de consistencia entre la fórmula de compresión pura y el integratorio de fibras.

---

## 9. Primera demanda-capacidad

Se ubican las **demandas reales** (de `combinaciones.json`, superposición validada en §4) sobre las envolventes P-M de las secciones 6–7. Momento efectivo biaxial para la columna: `M_ef = √(My² + Mz²)` (primera aproximación; la envolvente usada es uniaxial).

| Elemento | Combo | P [kN] | My [kN·m] | Mz [kN·m] | M_ef [kN·m] | M_nom(cap) [kN·m] | P/(0,8·Pn0) | **DCR** = M_ef/M_nom |
|---|---|---|---|---|---|---|---|---|
| Columna F‑2 (11020) · niv. 1 | U1 (1,4G) | 3 496,1 | 1,3 | −15,0 | 15,0 | 1 576,7 | 0,26 | 0,010 |
| Columna F‑2 (11020) · niv. 1 | U2 (1,2G+1,6Q) | 5 512,7 | 2,0 | −23,6 | 23,7 | 1 773,6 | 0,41 | 0,013 |
| Columna F‑2 (11020) · niv. 1 | U3 (1,2G+Q+EX) | 4 609,9 | 1 155,2 | −194,5 | 1 171,4 | 1 694,8 | 0,34 | **0,69** |
| Muro núcleo 11033 · niv. 1 | U3 (1,2G+Q+EX) | 0,0 | 800,7 | 3,0 | 800,7 | 224,3 | — | **3,57** ⚠ |
| Muro núcleo 11033 · niv. 1 | U4 (0,9G+EY) | 0,0 | −99,2 | 40,5 | 107,2 | 224,3 | — | 0,48 |

Lectura:

- **Columna OK.** El caso más exigente es U3 (sísmico +X) con **DCR = 0,69**: el punto de demanda (P≈4 610 kN, M≈1 171 kN·m) cae dentro de la envolvente con ~31% de holgura. La combinación gravitatoria U2 controla la **axial** (P/(0,8·Pn0) = 0,41 < 1).
- **Muro ⚠ es un marcador de revisión del modelo, no un diagnóstico de colapso.** Con el pier del modelo (L=1,0 m), la demanda en el plano del núcleo (M = 800,7 kN·m, P ≈ 0) supera la capacidad de rigidez del tramo (224,3 kN·m → DCR = 3,57). Causas y límites del chequeo: (i) el muro del modelo es un **pier de 1,0 m**, no la pared real; por eso la capacidad es de tramo y no de la pared completa; (ii) los muros no reciben carga gravitatoria en el modelo (P=0), lo que los deja en la rama baja de la envolvente; (iii) la demanda elástica corresponde al cortante sísmico de la masa sin reducción. La corrección requiere **re-modelar el muro con su longitud real dentro del propio modelo elástico** (no basta escalar la sección en el post-proceso: mezcla demanda del pier con capacidad de línea) y repartir la carga gravitatoria del núcleo (ver §10).

---

## 10. Uso de IA

Como trabajo académico, se explicita qué se delegó a IA generativa y qué criterios requirieron revisión humana:

1. **Descomposición de `qG` en casos G y Q.** La carga nominal de catálogo (10,15 kN/m²) **incluye sobrecarga**; la IA propuso (y el grupo validó) descomponerla en G = 6,225 (3,675 losa + 2,550 adic.) y Q = 3,92 kN/m², verificando que la suma reconstruye el valor nominal con ψ de masa sísmica.
2. **Reuso de áreas tributarias para Q**: decisión geométrica (una sola malla tributaria para todas las cargas), contrastada por el chequeo de conservación cerrado de §2.
3. **Parámetros sísmicos**: A0, S, I y R adoptados con más de una justificación; el **C adoptado (0,25)** se eligió deliberadamente por encima del C calculado (0,21) como criterio conservador y debe revisarse frente al espectro de la normativa (la fórmula de período corto es una simplificación).
4. **Armadura asumida**: 12φ25 (columna) y 2 mallas φ12@200 (muro) son **supuestos de diseño del grupo** para la cadena demanda-capacidad; deben confrontarse con los planos antes del diseño definitivo.
5. **Convención de dirección del muro y momento biaxial**: la demanda del muro se tomó de su momento local dominante (My) y la de la columna con `√(My²+Mz²)`; ambas convenciones quedan señaladas para re-chequeo.
6. **Detección de inconsistencia del modelo por la IA**: al cerrar la demanda-capacidad, el proceso detectó que (a) los muros tienen P≈0 (no están en el camino gravitatorio real) y (b) la DCR > 1 del pier del muro. Se **descartó** la tentación de escalar la sección a la longitud continua inferida (9,42 m → 60 000 kN) por mezclar demanda del pier con capacidad de toda la línea; la corrección legítima es **re-modelar el muro con su longitud real** y repartir la carga gravitatoria, tarea de la próxima iteración.
7. **Validación sistemática**: la superposición (0,0000% de error), la conservación piso a piso (≤0,03 %) y la convergencia con el nº de fibras (<0,2 %) se generaron como *checks* obligatorios de cualquier script asistido por IA antes de usar sus números.

---

## Archivos y artefactos de la semana

| Artefacto | Descripción |
|---|---|
| `scripts/analisis_casos_base.py` | Casos base G/Q/EX/EY, sismo pseudo-estático y combinaciones U1–U4 (superposición vs directo) |
| `scripts/momento_curvatura.py` | Análisis por fibras M-φ (Python puro, sin OpenSees) |
| `scripts/curvas_pm.py` | Envolventes P-M columna y muro, verificación RC y demanda-capacidad |
| `reports/salidas/casos_base_{G,Q,EX,EY}.json` | Esfuerzos, reacciones y desplazamientos por caso |
| `reports/salidas/sismo_pseudoestatico.json` | Pesos, fuerzas, V0, desplazamientos, derivas, conservación |
| `reports/salidas/combinaciones.json` | Superposición vs corrida directa por combo |
| `reports/salidas/momento_curvatura.json` | Resultados M-φ y sensibilidad |
| `reports/salidas/curvas_pm.json` | Verificación RC y DCR |
| `reports/fig/mf_curva.png`, `pm_columna.png`, `pm_muro.png` | Figuras de la entrega |

---

*Reporte redactado con el apoyo de IA generativa (asistente de código OpenCode) y revisado por el grupo; los criterios numéricos de §3, §5 y §10 fueron decididos por los integrantes de la entrega.*