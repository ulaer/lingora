"""Kural tabanlı EN/TR cümle kurucu (model kullanmaz).

Biçimler (form): prog, prog_neg, prog_q, past, past_neg, past_q, fut, fut_neg, fut_q.
Türkçe fiil biçimleri manifestte 3. tekil olarak tutulur (yiyor, yedi, yiyecek, yemiyor, yemedi,
yemeyecek); çoğul eki ve soru eki ünlü uyumuyla burada eklenir.
"""

FRONT = set("eiöü")
VOWELS = set("aeıioöuü")
VOICELESS = set("çfhkpsşt")
FORMS = ["prog", "prog_neg", "prog_q", "past", "past_neg", "past_q", "fut", "fut_neg", "fut_q"]
TIME_WORD = {"past": ("yesterday", "dün"), "fut": ("tomorrow", "yarın")}


def last_vowel(word):
    for ch in reversed(word.lower()):
        if ch in VOWELS:
            return ch
    return "e"


def two_way(word):  # e / a
    return "e" if last_vowel(word) in FRONT else "a"


def four_way(word):  # i / ı / u / ü
    return {"e": "i", "i": "i", "a": "ı", "ı": "ı", "o": "u", "u": "u", "ö": "ü", "ü": "ü"}[last_vowel(word)]


def plural(word):
    return word + "l" + two_way(word) + "r"


def q_particle(word):
    return "m" + four_way(word)


def copula_past(word):
    """yorgun -> yorgundu, mutlu -> mutluydu, sıkılmış -> sıkılmıştı"""
    last = word[-1].lower()
    if last in VOWELS:
        return word + "yd" + four_way(word)
    return word + ("t" if last in VOICELESS else "d") + four_way(word)


def lower_subject(subj):
    """Soru cümlesinde özne başa gelmez: 'The dog' -> 'the dog', 'He' -> 'he'; özel adlar korunur."""
    if subj["en"].split()[0] in ("The", "He", "She", "It", "They", "My", "Two"):
        return subj["en"][0].lower() + subj["en"][1:]
    return subj["en"]


def join(*parts):
    return " ".join(p for p in parts if p)


def sentence(text, question=False):
    text = text.strip()
    return text[0].upper() + text[1:] + ("?" if question else ".")


def verb_clause(form, subj, verb, obj_en="", obj_tr="", place=None, tod=None):
    """Bir fiil cümlesi kurar. subj: {en, tr, plural, tr_plural}; verb: manifest 'verbs' girdisi.
    place: (en, tr_loc) ya da None; tod: günün vakti (en, tr) ya da None."""
    base, tense = form.split("_")[0], form
    pl = subj.get("plural", False)
    tr_pl = subj.get("tr_plural", pl)
    en_v, tr_v = verb["en"], verb["tr"]
    prep = en_v.get("prep", "")
    p_en, p_tr = place if place else ("", "")
    t_en, t_tr = tod if tod else ("", "")
    time_en, time_tr = TIME_WORD.get(base, ("", ""))
    neg, q = tense.endswith("_neg"), tense.endswith("_q")
    # --- English ---
    tail = join(prep, obj_en, p_en, t_en, time_en)
    if base == "prog":
        if en_v.get("stative"):  # want: simple present
            if q:
                en = join("Do" if pl else "Does", lower_subject(subj), en_v["base"], tail)
            else:
                en = join(subj["en"], ("don't " if pl else "doesn't ") + en_v["base"] if neg
                          else (en_v["base"] if pl else en_v["s3"]), tail)
        else:
            be = "are" if pl else "is"
            en = join(be.capitalize(), lower_subject(subj), en_v["ing"], tail) if q else \
                join(subj["en"], be + ("n't" if neg else ""), en_v["ing"], tail)
    elif base == "past":
        en = join("Did", lower_subject(subj), en_v["base"], tail) if q else \
            join(subj["en"], "didn't " + en_v["base"] if neg else en_v["past"], tail)
    else:
        en = join("Will", lower_subject(subj), en_v["base"], tail) if q else \
            join(subj["en"], "won't " + en_v["base"] if neg else "will " + en_v["base"], tail)
    # --- Türkçe ---
    key = base + ("_neg" if neg else "")
    v = tr_v[key]
    if tr_pl:
        v = plural(v)
    if q:
        v = join(v, q_particle(v))
    tr = join(subj["tr"], time_tr, t_tr, p_tr, obj_tr, v)
    return sentence(en, q), sentence(tr, q)


def adjective_clause(form, subj, adj, place=None):
    """Duygu/durum: 'Tom is tired.' / 'Tom yorgun.'  (prog=şimdi, past=dün)"""
    base = form.split("_")[0]
    neg, q = form.endswith("_neg"), form.endswith("_q")
    pl = subj.get("plural", False)
    p_en, p_tr = place if place else ("", "")
    if base == "prog":
        be = "are" if pl else "is"
        en = join(be.capitalize(), lower_subject(subj), adj["en"], p_en) if q else \
            join(subj["en"], be + ("n't" if neg else ""), adj["en"], p_en)
        if q:
            tr = join(subj["tr"], p_tr, adj["tr"], q_particle(adj["tr"]))
        else:
            tr = join(subj["tr"], p_tr, adj["tr"], "değil" if neg else "")
    else:  # past
        be = "were" if pl else "was"
        en = join(be.capitalize(), lower_subject(subj), adj["en"], p_en, "yesterday") if q else \
            join(subj["en"], be + ("n't" if neg else ""), adj["en"], p_en, "yesterday")
        if q:
            qp = q_particle(adj["tr"])
            tr = join(subj["tr"], "dün", p_tr, adj["tr"], qp + "yd" + four_way(qp))
        elif neg:
            tr = join(subj["tr"], "dün", p_tr, adj["tr"], "değildi")
        else:
            tr = join(subj["tr"], "dün", p_tr, copula_past(adj["tr"]))
    return sentence(en, q), sentence(tr, q)


def weather_clause(form, w, place=None):
    """'It is raining in the park.' / 'Parkta yağmur yağıyor.'  w: manifest 'weather' girdisi."""
    base = form.split("_")[0]
    neg, q = form.endswith("_neg"), form.endswith("_q")
    p_en, p_tr = place if place else ("", "")
    time_en, time_tr = TIME_WORD.get(base, ("", ""))
    e = w["en"]
    if w["kind"] == "verb":  # rain, snow
        if base == "prog":
            en = join("Is it", e["ing"], p_en) if q else join("It", "isn't" if neg else "is", e["ing"], p_en)
        elif base == "past":
            en = join("Did it", e["base"], p_en, time_en) if q else \
                join("It", "didn't " + e["base"] if neg else e["past"], p_en, time_en)
        else:
            en = join("Will it", e["base"], p_en, time_en) if q else \
                join("It", "won't" if neg else "will", e["base"], p_en, time_en)
        v = w["tr"][base + ("_neg" if neg else "")]
        if q:
            v = join(v, q_particle(v))
        tr = join(time_tr, p_tr, w["tr"]["noun"], v)
    else:  # sunny, cloudy, windy (sıfat)
        adj = w["tr"]["adj"]
        if base == "prog":
            en = join("Is it", e["adj"], p_en) if q else join("It", "isn't" if neg else "is", e["adj"], p_en)
            tr = join(p_tr, "hava", adj, q_particle(adj) if q else ("değil" if neg else ""))
        elif base == "past":
            en = join("Was it", e["adj"], p_en, time_en) if q else \
                join("It", "wasn't" if neg else "was", e["adj"], p_en, time_en)
            if q:
                qp = q_particle(adj)
                tr = join(time_tr, p_tr, "hava", adj, qp + "yd" + four_way(qp))
            else:
                tr = join(time_tr, p_tr, "hava", adj + " değildi" if neg else copula_past(adj))
        else:
            en = join("Will it be", e["adj"], p_en, time_en) if q else \
                join("It", "won't" if neg else "will", "be", e["adj"], p_en, time_en)
            v = "olmayacak" if neg else "olacak"
            tr = join(time_tr, p_tr, "hava", adj, v, q_particle(v) if q else "")
    return sentence(en, q), sentence(tr, q)


def badges_for(form):
    out = []
    base = form.split("_")[0]
    if base == "past":
        out.append("cal_yesterday")
    if base == "fut":
        out.append("cal_tomorrow")
    if form.endswith("_neg"):
        out.append("badge_not")
    if form.endswith("_q"):
        out.append("badge_question")
    return out
