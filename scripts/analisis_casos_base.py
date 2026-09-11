"""
P1A3 - AVANCE: casos base y curvas de interacion.

Analisis de casos base del edificio 3D (OpenSeesPy, ndm=3 ndf=6):
    G  : carga muerta  (PP losa + PM adicional)
    Q  : carga viva    (sobrecarga de uso, reusando areas tributarias)
    EX : sismo pseudoestatico en +X (NCh433 simplificado)
    EY : sismo pseudoestatico en +Y

Para cada caso se corre un modelo independiente (wipe -> model -> cargas ->
analisis) y se extraen: esfuerzos por elemento, reacciones en la base y
desplazamientos/rotaciones de los diafragmas (nodo maestro).

Ademas se evaluan combinaciones mediante SUPERPOCISION de los casos base y se
comparan con una corrida DIRECTA de cada combinacion en OpenSees (los resultados
deben coincidir por linealidad).

Salidas (en reports/salidas/):
    casos_base_{G,Q,EX,EY}.json     -> esfuerzos, reacciones y desplazamientos
    sismo_pseudoestatico.json       -> pesos, fuerzas por piso, corte basal, drifts
    combinaciones.json              -> superposicion + corrida directa + comparacion

USO:
    python scripts/analisis_casos_base.py

REQUIERE: openseespy, numpy
"""
import json
import os
import sys
import numpy as np

import openseespy.opensees as ops

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SALIDAS = os.path.join(RAIZ, "reports", "salidas")

FUNDACIONES = os.path.join(RAIZ, "fundaciones.json")
SECCIONES = os.path.join(RAIZ, "secciones_verticales.json")
ELEMENTOS = os.path.join(RAIZ, "elementos_verticales.json")
CARGAS = os.path.join(RAIZ, "cargas_piso.json")
VIGAS = os.path.join(RAIZ, "vigas_tributarias.json")

# ---------------------------------------------------------------------------
# Parametros del modelo (coherentes con model_edificio.py)
# ---------------------------------------------------------------------------
H_PISO = 3.96
N_NIVELES = 5
SECTION_VIGA = 300
TRANSF_VERTICAL = 1
TRANSF_HORIZONTAL = 2

# Valores de cargas (kN/m2) - descomposicion de qG = 10.15 kN/m2
CARGA_MUERTA = 6.225      # G = PP losa (3.675) + PM adic (2.55)
CARGA_VIVA = 3.92         # Q = sobrecarga de uso (SC = 400 kg/m2)
GAMMA_C = 24.5            # kN/m3  (peso_unitario_kN_m3 de cargas_piso.json)

# --- Parametros sismo pseudoestatico (supuestos documentados en semana03.md) --
A0_G = 0.40               # aceleracion efectiva (zona 3, NCh433)
SOIL_S = 1.05             # suelo tipo II
I_IMPORT = 1.0            # edificio normal
R_COEF = 5.5              # muros de hormigon armado (NRH)
C_SISMICO = round(2.75 * A0_G * SOIL_S * I_IMPORT / R_COEF, 4)   # = 0.21
C_USADO = 0.25            # adoptado (redondeado conservador, periodo corto T<T0)
PSI_LL = 0.25             # fraccion de carga viva en peso sismico (NCh433)

# ---------------------------------------------------------------------------
# Combinaciones (factores mayoracion, ACI 318 / NCh)
# ---------------------------------------------------------------------------
COMBOS = {
    "U1": {"nombre": "1.4 G", "coefs": {"G": 1.4}},
    "U2": {"nombre": "1.2 G + 1.6 Q", "coefs": {"G": 1.2, "Q": 1.6}},
    "U3": {"nombre": "1.2 G + 1.0 Q + 1.0 EX", "coefs": {"G": 1.2, "Q": 1.0, "EX": 1.0}},
    "U4": {"nombre": "0.9 G + 1.0 EY", "coefs": {"G": 0.9, "EY": 1.0}},
}

NOMBRES_NIVEL = {1: "1° Subterráneo", 2: "1º Piso", 3: "2º Piso",
                 4: "3º Piso", 5: "4º Piso"}


# ---------------------------------------------------------------------------
# Helpers de tags (idem model_edificio.py)
# ---------------------------------------------------------------------------
def z_nivel(k):
    return k * H_PISO


def tag_losa(k, base):
    return 1000 + (k - 1) * 100 + base


def tag_master(k):
    return 9500 + k


def leer(ruta):
    with open(ruta, encoding="utf-8") as f:
        return json.load(f)


def etiqueta_viga_nivel(vigas, idx, k):
    """Tag del elemento viga en el nivel k (coherente con model_edificio)."""
    grp = "X" if vigas[idx]["dir"] == "X" else "Y"
    base_e = 20000 if grp == "X" else 30000
    n_x = len([v for v in vigas if v["dir"] == "X"])
    i_grp = idx if grp == "X" else (idx - n_x)
    return base_e + k * 100 + i_grp


def definir_voladizos():
    """Mismas configs de voladizos que model_edificio.definir_voladizos()."""
    return [
        {"k": 5, "grupo": "Y", "i_grp": [10, 11], "ancho_m": 5.85,
         "origen": "Plano 2017-67-300 - voladizo ultimo piso (dir X/I')"},
        {"k": 3, "grupo": "X", "i_grp": list(range(10, 15)), "ancho_m": 4.1,
         "origen": "Plano 2017-67-306 - voladizo piso 2 (dir Y hacia eje 1)"},
        {"k": 3, "grupo": "X", "i_grp": list(range(0, 5)), "ancho_m": 0.8,
         "origen": "Plano 2017-67-306 - voladizo piso 2 (dir Y hacia eje 3)"},
    ]


# ---------------------------------------------------------------------------
# Construccion del modelo
# ---------------------------------------------------------------------------
def construir_modelo(apoyos, sec, elementos, vigas, cm):
    """wipe + model + nodos + elementos + diafragmas (estructura sin cargas)."""
    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)

    for mat in sec["materiales"]:
        ops.uniaxialMaterial("Elastic", mat["matTag"], mat["E_kPa"])
    for s in sec["secciones"]:
        ops.section("Elastic", s["sectionTag"], s["E_kPa"], s["A_m2"],
                    s["Iz_m4"], s["Iy_m4"], s["G_kPa"], s["J_m4"])

    # Nodos base + apoyos
    for a in apoyos:
        ops.node(a["nodeTag"], a["x"] / 100.0, a["y"] / 100.0, 0.0)
        ops.fix(a["nodeTag"], *a["restraints"])

    # Nodos de losa + maestro por nivel
    for k in range(1, N_NIVELES + 1):
        zn = z_nivel(k)
        for a in apoyos:
            ops.node(tag_losa(k, a["nodeTag"]), a["x"] / 100.0, a["y"] / 100.0, zn)
        ops.node(tag_master(k), cm[0], cm[1], zn)
        ops.fix(tag_master(k), 0, 0, 1, 1, 1, 0)

    ops.geomTransf("Linear", TRANSF_VERTICAL, 1.0, 0.0, 0.0)
    ops.geomTransf("Linear", TRANSF_HORIZONTAL, 0.0, 0.0, 1.0)

    # Elementos verticales (tramo por nivel)
    for a in elementos:
        base = a["iNode"]
        for k in range(1, N_NIVELES + 1):
            ini = base if k == 1 else tag_losa(k - 1, base)
            fin = tag_losa(k, base)
            ops.element("elasticBeamColumn", 10000 + k * 1000 + base,
                        ini, fin, a["sectionTag"], TRANSF_VERTICAL)

    # Vigas por nivel
    for k in range(1, N_NIVELES + 1):
        off = (k - 1) * 100
        for idx, v in enumerate(vigas):
            ini = v["iNode"] + off
            fin = v["jNode"] + off
            grp = "X" if v["dir"] == "X" else "Y"
            base_e = 20000 if grp == "X" else 30000
            n_x = len([x for x in vigas if x["dir"] == "X"])
            i_grp = idx if grp == "X" else (idx - n_x)
            ops.element("elasticBeamColumn", base_e + k * 100 + i_grp,
                        ini, fin, SECTION_VIGA, TRANSF_HORIZONTAL)

    # Diafragmas rigidos
    for k in range(1, N_NIVELES + 1):
        slaves = [tag_losa(k, a["nodeTag"]) for a in apoyos]
        ops.rigidDiaphragm(3, tag_master(k), *slaves)


def aplicar_uniforme(vigas, q, voladizos, tag_patron=100):
    """Carga repartida w = q*A_trib/L + carga por voladizos (como model_edificio)."""
    ops.timeSeries("Linear", tag_patron)
    ops.pattern("Plain", tag_patron, tag_patron)
    for k in range(1, N_NIVELES + 1):
        for idx, v in enumerate(vigas):
            w = q * v["area_trib_m2"] / v["longitud_m"]
            etag = etiqueta_viga_nivel(vigas, idx, k)
            ops.eleLoad("-ele", etag, "-type", "beamUniform", 0.0, -w)
    for vol in voladizos:
        w_extra = q * vol["ancho_m"]
        for i_grp in vol["i_grp"]:
            if vol["grupo"] == "X":
                etag = 20000 + vol["k"] * 100 + i_grp
            else:
                etag = 30000 + vol["k"] * 100 + i_grp
            ops.eleLoad("-ele", etag, "-type", "beamUniform", 0.0, -w_extra)


def aplicar_sismo_nodal(fuerzas, dir, tag_patron=100):
    """Carga nodal en el nodo maestro de cada nivel (fuerzas dict k->kN)."""
    ops.timeSeries("Linear", tag_patron)
    ops.pattern("Plain", tag_patron, tag_patron)
    for k, F in fuerzas.items():
        fx = F if dir == "X" else 0.0
        fy = F if dir == "Y" else 0.0
        ops.load(tag_master(k), fx, fy, 0.0, 0.0, 0.0, 0.0)


def configurar_analisis():
    ops.system("BandSPD")
    ops.numberer("RCM")
    ops.constraints("Transformation")
    ops.algorithm("Newton")
    ops.integrator("LoadControl", 1.0)
    ops.analysis("Static")


# ---------------------------------------------------------------------------
# Extraccion de resultados
# ---------------------------------------------------------------------------
def extraer_elementos(apoyos, elementos, vigas):
    out = []
    for k in range(1, N_NIVELES + 1):
        for a in elementos:
            etag = 10000 + k * 1000 + a["iNode"]
            ini = a["iNode"] if k == 1 else tag_losa(k - 1, a["iNode"])
            fin = tag_losa(k, a["iNode"])
            f = ops.eleResponse(etag, "localForces")
            out.append({
                "elementTag": etag, "type": a["type"], "iNode": ini,
                "jNode": fin, "sectionTag": a["sectionTag"], "nivel": k,
                "descripcion": f"{a['descripcion']} · nivel {k}",
                "P": f[0], "Vy": f[1], "Vz": f[2], "My": f[4], "Mz": f[5],
                "Vzj": f[8], "Vyj": f[7],
            })
    n_x = len([v for v in vigas if v["dir"] == "X"])
    for k in range(1, N_NIVELES + 1):
        for idx, v in enumerate(vigas):
            grp = "X" if v["dir"] == "X" else "Y"
            base_e = 20000 if grp == "X" else 30000
            i_grp = idx if grp == "X" else (idx - n_x)
            etag = base_e + k * 100 + i_grp
            ini = v["iNode"] + (k - 1) * 100
            fin = v["jNode"] + (k - 1) * 100
            f = ops.eleResponse(etag, "localForces")
            out.append({
                "elementTag": etag, "type": "viga", "iNode": ini, "jNode": fin,
                "sectionTag": SECTION_VIGA, "nivel": k,
                "descripcion": f"Viga dir {v['dir']} L={v['longitud_m']}m · nivel {k}",
                "P": f[0], "Vy": f[1], "Vz": f[2], "My": f[4], "Mz": f[5],
                "Vzj": f[8], "Vyj": f[7],
            })
    return out


def extraer_reacciones(apoyos):
    ops.reactions()
    salida, suma = [], [0.0] * 6
    for a in apoyos:
        reac = [ops.nodeReaction(a["nodeTag"], i + 1) for i in range(6)]
        suma = [s + r for s, r in zip(suma, reac)]
        salida.append({"nodeTag": a["nodeTag"], "x_cm": a["x"], "y_cm": a["y"],
                       "R": reac, "descripcion": a["descripcion"]})
    return salida, suma


def extraer_desplazamientos(apoyos, cm, nivel_corner=4):
    """Desplazamientos de los nodos maestros + verificacion de rigidez del
    diafragma con un nodo esquinero (tag_losa(k, nivel_corner))."""
    levels = {}
    for k in range(1, N_NIVELES + 1):
        u = ops.nodeDisp(tag_master(k))
        levels[k] = {
            "z_m": z_nivel(k),
            "ux_master": u[0], "uy_master": u[1], "uz_master": u[2],
            "rx_master": u[3], "ry_master": u[4], "rz_master": u[5],
        }
        # nodo esquinero (slave): verificar v = v_m + theta x r
        a = [ap for ap in apoyos if ap["nodeTag"] == nivel_corner][0]
        us = ops.nodeDisp(tag_losa(k, nivel_corner))
        x_s, y_s = a["x"] / 100.0, a["y"] / 100.0
        pred_ux = u[0] - u[5] * (y_s - cm[1])
        pred_uy = u[1] + u[5] * (x_s - cm[0])
        levels[k]["ux_esquina"] = us[0]
        levels[k]["uy_esquina"] = us[1]
        levels[k]["pred_ux_esquina"] = pred_ux
        levels[k]["pred_uy_esquina"] = pred_uy
    return levels


def correr_caso(nombre, apoyos, sec, elementos, vigas, cm, qG, qQ, fx=None,
                fy=None):
    """Corre un caso con el modelo fresco y devuelve resultados."""
    construir_modelo(apoyos, sec, elementos, vigas, cm)
    voladizos = definir_voladizos()
    if nombre in ("G", "Q"):
        aplicar_uniforme(vigas, qG if nombre == "G" else qQ, voladizos)
    elif nombre in ("EX", "EY"):
        fuerzas = fx if nombre == "EX" else fy
        aplicar_sismo_nodal(fuerzas, "X" if nombre == "EX" else "Y")
    else:
        raise ValueError(nombre)

    configurar_analisis()
    ok = ops.analyze(1)
    assert ok == 0, f"El analisis {nombre} no convergio (codigo {ok})"

    elementos_r = extraer_elementos(apoyos, elementos, vigas)
    reac, suma = extraer_reacciones(apoyos)
    desp = extraer_desplazamientos(apoyos, cm)
    return {
        "caso": nombre, "analisis_ok": True,
        "suma_reacciones": [round(s, 3) for s in suma],
        "elementos": elementos_r, "reacciones": reac, "desplazamientos": desp,
    }


def fuerza_por_nivel(W, zs):
    """F_i = V0 * (W_i*h_i) / sum(W_j*h_j)."""
    if isinstance(W, dict):
        wh = {k: W[k] * zs[k] for k in zs}
    else:
        wh = {k: W * zs[k] for k in zs}
    s = sum(wh.values())
    return {k: wh[k] / s for k in zs}


# ---------------------------------------------------------------------------
# Superposicion de combinaciones
# ---------------------------------------------------------------------------
def superponer(resultados, coefs):
    """Combina los casos base: interaccion lineal -> suma de esfuerzos."""
    cols = ["P", "Vy", "Vz", "My", "Mz"]
    super = {}
    tags = [e["elementTag"] for e in resultados[list(coefs.keys())[0]]["elementos"]]
    for tag in tags:
        acc = {c: 0.0 for c in cols}
        for caso, coef in coefs.items():
            el = next(e for e in resultados[caso]["elementos"]
                      if e["elementTag"] == tag)
            for c in cols:
                acc[c] += coef * el[c]
        super[tag] = acc
    return super


def comparar_superpuesta_directa(tag, super_vals, direct_vals, coefs):
    filas = []
    for c in ["P", "My", "Mz"]:
        sv = super_vals.get(tag, {}).get(c, 0.0)
        dv = direct_vals.get(tag, {}).get(c, 0.0)
        den = max(abs(dv), 1e-6)
        filas.append({"componente": c, "superpuesto": sv, "directo": dv,
                      "dif_abs": sv - dv,
                      "dif_pct": (sv - dv) / den * 100.0})
    return filas


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------
def main():
    os.makedirs(SALIDAS, exist_ok=True)
    apoyos = leer(FUNDACIONES)
    sec = leer(SECCIONES)
    elementos = leer(ELEMENTOS)
    vigas = leer(VIGAS)["vigas"]

    xs = [a["x"] / 100.0 for a in apoyos]
    ys = [a["y"] / 100.0 for a in apoyos]
    cm = ((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0)

    a_piso = leer(CARGAS)["a_piso_m2"]

    # --- Pesos sismicos por piso ------------------------------------------
    n_col = sum(1 for e in elementos if e["type"] == "columna")
    n_mur = sum(1 for e in elementos if e["type"] == "muro")
    sum_l_vigas = sum(v["longitud_m"] for v in vigas)
    peso_estructura_por_piso = GAMMA_C * (
        n_col * 0.49 * H_PISO + n_mur * 0.20 * 1.0 * H_PISO
        + sum_l_vigas * 0.48
    )
    W_por_piso = a_piso * (CARGA_MUERTA + PSI_LL * CARGA_VIVA) \
        + peso_estructura_por_piso
    W_total = W_por_piso * N_NIVELES
    V0 = C_USADO * W_total
    fuerzas_X = fuerza_por_nivel(W_por_piso,
                                 {k: z_nivel(k) for k in range(1, N_NIVELES + 1)})
    fuerzas_X = {k: f * V0 for k, f in fuerzas_X.items()}
    fuerzas_Y = dict(fuerzas_X)

    sismo_data = {
        "parametros": {
            "A0_g": A0_G, "S_suelo": SOIL_S, "I_importancia": I_IMPORT,
            "R": R_COEF, "C_calculado": C_SISMICO, "C_adoptado": C_USADO,
            "psi_carga_viva": PSI_LL,
            "formula": "V0 = C*sum(Wi) ; Fi = V0*(Wi*hi)/sum(Wj*hj) ; "
                       "Wi = A_piso*(G + psi*Q) + peso prop. estruct.",
        },
        "a_piso_m2": a_piso,
        "carga_muerta_kN_m2": CARGA_MUERTA,
        "carga_viva_kN_m2": CARGA_VIVA,
        "peso_estructura_por_piso_kN": round(peso_estructura_por_piso, 1),
        "W_por_piso_kN": round(W_por_piso, 1),
        "w_pisos_kN": {k: round(W_por_piso, 1) for k in range(1, N_NIVELES + 1)},
        "W_total_kN": round(W_total, 1),
        "V0_kN": round(V0, 1),
        "fuerzas_por_piso_kN": {k: round(f, 1) for k, f in fuerzas_X.items()},
    }

    print("=" * 86)
    print("CASOS BASE: G, Q, EX, EY  (edificio 3D, OpenSeesPy)")
    print("=" * 86)
    print(f"  G = {CARGA_MUERTA} kN/m2 (PP losa + PM adic.)   "
          f"Q = {CARGA_VIVA} kN/m2 (SC)")
    print(f"  Peso sismico por piso : {peso_estructura_por_piso:,.0f} (est.) + "
          f"{a_piso*(CARGA_MUERTA+PSI_LL*CARGA_VIVA):,.0f} (losa/muerto+0.25Q) "
          f"= {W_por_piso:,.0f} kN")
    print(f"  W_total = {W_total:,.0f} kN | V0 = C*W = {C_SISMICO}*{W_total/1000:,.1f} "
          f"MN = {V0:,.1f} kN")
    print("  Fuerzas por nivel (kN):")
    for k in range(1, N_NIVELES + 1):
        print(f"    k={k} ({NOMBRES_NIVEL[k]:>13})  z={z_nivel(k):.2f} m  "
              f"W={W_por_piso:,.0f} kN  F={fuerzas_X[k]:,.1f} kN")
    print()

    # --- Correr los 4 casos base -------------------------------------------
    resultados = {}
    for nombre in ("G", "Q"):
        r = correr_caso(nombre, apoyos, sec, elementos, vigas, cm,
                        qG=CARGA_MUERTA, qQ=CARGA_VIVA)
        resultados[nombre] = r
        ruta = os.path.join(SALIDAS, f"casos_base_{nombre}.json")
        with open(ruta, "w", encoding="utf-8") as f:
            json.dump(_redondear_num(r), f, ensure_ascii=False, indent=2)
        print(f"  Caso {nombre}: ΣRz = {r['suma_reacciones'][2]:,.1f} kN  "
              f"(conservacion: {CARGA_MUERTA if nombre=='G' else CARGA_VIVA}*"
              f"{a_piso:.0f}*{N_NIVELES} = "
              f"{(CARGA_MUERTA if nombre=='G' else CARGA_VIVA)*a_piso*N_NIVELES:,.1f} kN)")

    for nombre, fuerzas in (("EX", fuerzas_X), ("EY", fuerzas_Y)):
        r = correr_caso(nombre, apoyos, sec, elementos, vigas, cm,
                        qG=0.0, qQ=0.0, fx=fuerzas, fy=fuerzas)
        resultados[nombre] = r
        ruta = os.path.join(SALIDAS, f"casos_base_{nombre}.json")
        with open(ruta, "w", encoding="utf-8") as f:
            json.dump(_redondear_num(r), f, ensure_ascii=False, indent=2)
        suma_x, suma_y = r["suma_reacciones"][0], r["suma_reacciones"][1]
        if nombre == "EX":
            corte_modelo = suma_x
            dir_txt = "X"
        else:
            corte_modelo = suma_y
            dir_txt = "Y"
        print(f"  Caso {nombre} (+{dir_txt}): corte basal del modelo "
              f"(ΣR_{dir_txt}) = {corte_modelo:,.1f} kN  vs V0 = {V0:,.1f} kN")

    # --- Conservacion de carga por piso (G y Q) ----------------------------
    # se verifica contra el propio modelo: la carga total de vigas del piso k
    # es sum(Vz_i + Vz_j) de sus 27 vigas; debe ser ~ q*A_piso (+ voladizos).
    conservacion = {}
    for nombre in ("G", "Q"):
        q = CARGA_MUERTA if nombre == "G" else CARGA_VIVA
        voladizos = definir_voladizos()
        por_piso = {}
        suma_wl_total = 0.0
        for k in range(1, N_NIVELES + 1):
            vig_k = [e for e in resultados[nombre]["elementos"]
                     if e["type"] == "viga" and e["nivel"] == k]
            suma_wl = sum(e["Vz"] + e["Vzj"] for e in vig_k)
            esperado = q * a_piso
            volad = sum(q * vol["ancho_m"] * _long_viga_vol(vol, vigas)
                        for vol in voladizos if vol["k"] == k)
            por_piso[k] = {
                "suma_wL_modelo_kN": round(suma_wl, 2),
                "q_A_piso_kN": round(esperado, 2),
                "voladizos_kN": round(volad, 2),
                "diferencia_kN": round(suma_wl - esperado - volad, 3),
                "nota": "suma(wL) del modelo (Vz_i+Vz_j de las 27 vigas) vs "
                        "q*A_piso + carga de voladizos del nivel",
            }
            suma_wl_total += suma_wl
        conservacion[nombre] = {
            "q_kN_m2": q,
            "por_piso": por_piso,
            "total_wL_modelo_kN": round(suma_wl_total, 2),
            "sigmaRz_kN": resultados[nombre]["suma_reacciones"][2],
            "solape_area_tributaria_m2_por_piso":
                sum(v["area_trib_m2"] for v in vigas),
        }
        print(f"  Conservacion {nombre}: Σ(w·L) en vigas = {suma_wl_total:,.1f} kN "
              f"  ==  ΣRz base = {resultados[nombre]['suma_reacciones'][2]:,.1f} kN")
    conservacion["resumen_qG"] = {
        "qG_suma_G_Q_kN_m2": CARGA_MUERTA + CARGA_VIVA,
        "qG_nominal_kN_m2": leer(CARGAS)["qG_kN_m2"],
    }

    # --- Drifts y desplazamientos sismicos --------------------------------
    for nombre in ("EX", "EY"):
        desp = resultados[nombre]["desplazamientos"]
        eje = "ux" if nombre == "EX" else "uy"
        drifts = {}
        prev = 0.0
        for k in range(1, N_NIVELES + 1):
            u = desp[k][f"{eje}_master"]
            drifts[k] = (u - prev) / H_PISO
            prev = u
        sismo_data[f"desplazamientos_{nombre.lower()}"] = {
            k: {"ux_m": desp[k]["ux_master"], "uy_m": desp[k]["uy_master"],
                "rz_m": desp[k]["rz_master"]}
            for k in range(1, N_NIVELES + 1)
        }
        sismo_data[f"derivas_{nombre.lower()}"] = {
            k: round(drifts[k], 6) for k in range(1, N_NIVELES + 1)
        }
    sismo_data["conservacion"] = conservacion

    ruta_s = os.path.join(SALIDAS, "sismo_pseudoestatico.json")
    with open(ruta_s, "w", encoding="utf-8") as f:
        json.dump(_redondear_num(sismo_data), f, ensure_ascii=False, indent=2)

    # --- Construir Y correr combinaciones ----------------------------------
    combinaciones = {"combinaciones": {}, "comparacion": {}}
    for cid, combo in COMBOS.items():
        super_vals = superponer(resultados, combo["coefs"])

        # corrida directa
        construir_modelo(apoyos, sec, elementos, vigas, cm)
        voladizos = definir_voladizos()
        ops.timeSeries("Linear", 100)
        ops.pattern("Plain", 100, 100)
        for k in range(1, N_NIVELES + 1):
            for idx, v in enumerate(vigas):
                wg = CARGA_MUERTA * v["area_trib_m2"] / v["longitud_m"]
                wq = CARGA_VIVA * v["area_trib_m2"] / v["longitud_m"]
                w = combo["coefs"].get("G", 0.0) * wg \
                    + combo["coefs"].get("Q", 0.0) * wq
                if abs(w) > 1e-9:
                    etag = etiqueta_viga_nivel(vigas, idx, k)
                    ops.eleLoad("-ele", etag, "-type", "beamUniform", 0.0, -w)
        for vol in voladizos:
            wg = CARGA_MUERTA * vol["ancho_m"]
            wq = CARGA_VIVA * vol["ancho_m"]
            w = combo["coefs"].get("G", 0.0) * wg \
                + combo["coefs"].get("Q", 0.0) * wq
            if abs(w) > 1e-9:
                for i_grp in vol["i_grp"]:
                    etag = (20000 if vol["grupo"] == "X" else 30000) \
                        + vol["k"] * 100 + i_grp
                    ops.eleLoad("-ele", etag, "-type", "beamUniform", 0.0, -w)
        if "EX" in combo["coefs"]:
            for k, F in fuerzas_X.items():
                ops.load(tag_master(k), combo["coefs"]["EX"] * F, 0.0,
                         0.0, 0.0, 0.0, 0.0)
        if "EY" in combo["coefs"]:
            for k, F in fuerzas_Y.items():
                ops.load(tag_master(k), 0.0, combo["coefs"]["EY"] * F,
                         0.0, 0.0, 0.0, 0.0)

        configurar_analisis()
        ok = ops.analyze(1)
        assert ok == 0, f"Combinacion {cid} no convergio ({ok})"
        direct = extraer_elementos(apoyos, elementos, vigas)
        direct_vals = {e["elementTag"]: e for e in direct}
        reac, suma = extraer_reacciones(apoyos)

        combinaciones["combinaciones"][cid] = {
            "nombre": combo["nombre"], "coefs": combo["coefs"],
            "suma_reacciones": [round(s, 3) for s in suma],
        }

        # elementos de comparacion
        cols_u = [e for e in direct if e["type"] == "columna"]
        vigas_u = [e for e in direct if e["type"] == "viga"]
        muros_u = [e for e in direct if e["type"] == "muro"]
        col_max = max(cols_u, key=lambda e: abs(e["P"]))
        viga_max = max(vigas_u, key=lambda e: abs(e["My"]))
        muro_max = max(muros_u, key=lambda e: max(abs(e["My"]), abs(e["Mz"])))
        sel = [col_max["elementTag"], viga_max["elementTag"],
               muro_max["elementTag"]]
        comparacion = []
        for tag in sel:
            filas = comparar_superpuesta_directa(
                tag, super_vals, direct_vals, combo["coefs"])
            # filas solo de componentes relevantes
            comparacion.append({"elementTag": tag,
                                "descripcion": direct_vals[tag]["descripcion"],
                                "filas": filas})
        combinaciones["comparacion"][cid] = comparacion

        print(f"  Combo {cid:>3} ({combo['nombre']:<24}) → ΣRz = "
              f"{suma[2]:,.1f} kN | comparacion en tags {sel}")

    ruta_c = os.path.join(SALIDAS, "combinaciones.json")
    with open(ruta_c, "w", encoding="utf-8") as f:
        json.dump(combinaciones, f, ensure_ascii=False, indent=2)

    ruta_s = os.path.join(SALIDAS, "sismo_pseudoestatico.json")
    with open(ruta_s, "w", encoding="utf-8") as f:
        json.dump(_redondear_num(sismo_data), f, ensure_ascii=False, indent=2)

    # --- Resumen de superposicion (tabla) ---------------------------------
    print()
    print("-" * 86)
    print("RESPUESTA SUPERPUESTA vs CORRIDA DIRECTA (OpenSees)")
    print(f"{'combo':>5}  {'elem':>7}  {'comp':>4}  {'superp':>10}  "
          f"{'directo':>10}  {'dif%':>8}")
    for cid, comps in combinaciones["comparacion"].items():
        for c in comps:
            for fila in c["filas"]:
                print(f"{cid:>5}  {c['elementTag']:>7}  {fila['componente']:>4}  "
                      f"{fila['superpuesto']:>10.3f}  {fila['directo']:>10.3f}  "
                      f"{fila['dif_pct']:>8.4f}")
    print("=" * 86)
    print(f"Salidas: {SALIDAS}")


def _long_viga_vol(vol, vigas):
    """Longitud total de las vigas de borde que reciben el voladizo."""
    n_x = len([v for v in vigas if v["dir"] == "X"])
    L = 0.0
    for i_grp in vol["i_grp"]:
        idx = i_grp if vol["grupo"] == "X" else n_x + i_grp
        L += vigas[idx]["longitud_m"]
    return L


def _redondear_num(o):
    """Redondea floats anidados para JSON compacto."""
    if isinstance(o, float):
        return round(o, 4)
    if isinstance(o, dict):
        return {k: _redondear_num(v) for k, v in o.items()}
    if isinstance(o, list):
        return [_redondear_num(v) for v in o]
    return o


if __name__ == "__main__":
    main()