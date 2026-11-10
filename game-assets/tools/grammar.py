"""Kural tabanlı EN/TR cümle kurucu (model kullanmaz).

Biçimler (form): prog, prog_neg, prog_q, past, past_neg, past_q, fut, fut_neg, fut_q.
Türkçe fiil biçimleri manifestte 3. tekil olarak tutulur (yiyor, yedi, yiyecek, yemiyor, yemedi,
yemeyecek); çoğul eki, kişi eki ve soru eki ünlü uyumuyla burada eklenir.

v4: kişi (1s/2s/1p/2p: koşuyorum, koştun, koşacağız, koşuyor muyum?, yorgunsunuz), geniş zaman
(aor/aor_neg/aor_q: "Tom reads a book every day." / "Tom her gün bir kitap okur."), yetenek
(can/can_neg/can_q: "Tom can swim." / "Tom yüzebilir."). Geniş zaman ve yetenek TR biçimleri fiil başına
manifestte açık yazılır (tr.aor, tr.aor_neg, tr.can, tr.can_neg); yalnızca 3. kişide kullanılır.
"""

FRONT = set("eiöü")
VOWELS = set("aeıioöuü")
VOICELESS = set("çfhkpsşt")
FORMS = ["prog", "prog_neg", "prog_q", "past", "past_neg", "past_q", "fut", "fut_neg", "fut_q"]
TIME_WORD = {"past": ("yesterday", "dün"), "fut": ("tomorrow", "yarın"), "aor": ("every day", "her gün")}
PERSONS = ["1s", "2s", "1p", "2p"]
PERSON_PRONOUN = {"1s": ("I", "Ben"), "2s": ("You", "Sen"), "1p": ("We", "Biz"), "2p": ("You", "Siz")}
PERSON_PLURAL = {"1s": False, "2s": False, "1p": True, "2p": True}


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
    if subj["en"].split()[0] in ("The", "He", "She", "It", "They", "My", "Two", "You", "We"):
        return subj["en"][0].lower() + subj["en"][1:]
    return subj["en"]


def q_person(f, person):
    """Soru eki + kişi: f = ünlü (u/ı/i/ü). muyum / musun / muyuz / musunuz"""
    return "m" + f + {"1s": "y" + f + "m", "2s": "s" + f + "n", "1p": "y" + f + "z", "2p": "s" + f + "n" + f + "z"}[person]


def past_person(v, person):
    """koştu -> koştum / koştun / koştuk / koştunuz (3. tekil ünlüyle biter)"""
    f = four_way(v)
    return v + {"1s": "m", "2s": "n", "1p": "k", "2p": "n" + f + "z"}[person]


def prog_person(v, person):
    """koşuyor -> koşuyorum / koşuyorsun / koşuyoruz / koşuyorsunuz"""
    return v + {"1s": "um", "2s": "sun", "1p": "uz", "2p": "sunuz"}[person]


def fut_person(v, person):
    """koşacak -> koşacağım / koşacaksın / koşacağız / koşacaksınız (k -> ğ ünlüden önce)"""
    f = four_way(v)
    if person in ("1s", "1p"):
        return v[:-1] + "ğ" + f + ("m" if person == "1s" else "z")
    return v + "s" + f + "n" + (f + "z" if person == "2p" else "")


def copula_person(adj, person):
    """yorgun -> yorgunum / yorgunsun / yorgunuz / yorgunsunuz; mutlu -> mutluyum"""
    f = four_way(adj)
    y = "y" if adj[-1].lower() in VOWELS else ""
    return adj + {"1s": y + f + "m", "2s": "s" + f + "n", "1p": y + f + "z", "2p": "s" + f + "n" + f + "z"}[person]


def s3(base):
    """EN 3. tekil -s: read -> reads, watch -> watches, cry -> cries, turn on -> turns on"""
    first, _, rest = base.partition(" ")
    if first.endswith(("s", "sh", "ch", "x", "z", "o")):
        first += "es"
    elif first.endswith("y") and first[-2] not in "aeiou":
        first = first[:-1] + "ies"
    else:
        first += "s"
    return join(first, rest)


def en_be(person, plural=False, past=False):
    if past:
        return "was" if (person in ("1s", "3s") and not plural) else "were"
    return "am" if person == "1s" else ("are" if (plural or person != "3s") else "is")


def join(*parts):
    return " ".join(p for p in parts if p)


def sentence(text, question=False):
    text = text.strip()
    return text[0].upper() + text[1:] + ("?" if question else ".")


def verb_clause(form, subj, verb, obj_en="", obj_tr="", place=None, tod=None):
    """Bir fiil cümlesi kurar. subj: {en, tr, plural, tr_plural, person?}; verb: manifest 'verbs' girdisi.
    place: (en, tr_loc) ya da None; tod: günün vakti (en, tr) ya da None.
    subj['person'] 1s/2s/1p/2p ise kişi eki eklenir (prog/past/fut); aor ve can yalnızca 3. kişi."""
    base, tense = form.split("_")[0], form
    person = subj.get("person", "3s")
    pl = subj.get("plural", False)
    tr_pl = subj.get("tr_plural", pl)
    en_v, tr_v = verb["en"], verb["tr"]
    prep = en_v.get("prep", "")
    p_en, p_tr = place if place else ("", "")
    t_en, t_tr = tod if tod else ("", "")
    time_en, time_tr = TIME_WORD.get(base, ("", ""))
    neg, q = tense.endswith("_neg"), tense.endswith("_q")
    tail = join(prep, obj_en, p_en, t_en, time_en)
    # --- English ---
    if base == "prog":
        if en_v.get("stative"):  # want: simple present
            if q:
                en = join("Do" if pl else "Does", lower_subject(subj), en_v["base"], tail)
            else:
                en = join(subj["en"], ("don't " if pl else "doesn't ") + en_v["base"] if neg
                          else (en_v["base"] if pl else en_v["s3"]), tail)
        else:
            be = en_be(person, pl)
            if q:
                en = join(be.capitalize(), lower_subject(subj), en_v["ing"], tail)
            elif neg and person == "1s":
                en = join("I'm not", en_v["ing"], tail)
            else:
                en = join(subj["en"], be + ("n't" if neg else ""), en_v["ing"], tail)
    elif base == "past":
        en = join("Did", lower_subject(subj), en_v["base"], tail) if q else             join(subj["en"], "didn't " + en_v["base"] if neg else en_v["past"], tail)
    elif base == "fut":
        en = join("Will", lower_subject(subj), en_v["base"], tail) if q else             join(subj["en"], "won't " + en_v["base"] if neg else "will " + en_v["base"], tail)
    elif base == "aor":  # geniş zaman: Tom reads / doesn't read / Does Tom read ...?
        if q:
            en = join("Do" if pl else "Does", lower_subject(subj), en_v["base"], tail)
        else:
            en = join(subj["en"], ("don't " if pl else "doesn't ") + en_v["base"] if neg
                      else (en_v["base"] if pl else en_v.get("s3") or s3(en_v["base"])), tail)
    else:  # can
        tail_c = join(prep, obj_en, p_en, t_en)
        en = join("Can", lower_subject(subj), en_v["base"], tail_c) if q else             join(subj["en"], "can't" if neg else "can", en_v["base"], tail_c)
    # --- Türkçe ---
    key = base + ("_neg" if neg else "")
    v = tr_v[key]
    if person != "3s":
        if base == "past":
            v = past_person(v, person)
            if q:
                v = join(v, q_particle(v))
        else:
            if q:
                v = join(v, q_person(four_way(v), person))
            else:
                v = (prog_person if base == "prog" else fut_person)(v, person)
    else:
        if tr_pl:
            v = plural(v)
        if q:
            v = join(v, q_particle(v))
    tr = join(subj["tr"], time_tr, t_tr, p_tr, obj_tr, v)
    return sentence(en, q), sentence(tr, q)


def adjective_clause(form, subj, adj, place=None):
    """Duygu/durum: 'Tom is tired.' / 'Tom yorgun.'  (prog=şimdi, past=dün); kişi: 'I am tired.' / 'Ben yorgunum.'"""
    base = form.split("_")[0]
    neg, q = form.endswith("_neg"), form.endswith("_q")
    pl = subj.get("plural", False)
    person = subj.get("person", "3s")
    p_en, p_tr = place if place else ("", "")
    if base == "prog":
        be = en_be(person, pl)
        if q:
            en = join(be.capitalize(), lower_subject(subj), adj["en"], p_en)
        elif neg and person == "1s":
            en = join("I'm not", adj["en"], p_en)
        else:
            en = join(subj["en"], be + ("n't" if neg else ""), adj["en"], p_en)
        if person == "3s":
            if q:
                tr = join(subj["tr"], p_tr, adj["tr"], q_particle(adj["tr"]))
            else:
                tr = join(subj["tr"], p_tr, adj["tr"], "değil" if neg else "")
        else:
            if q:
                tr = join(subj["tr"], p_tr, adj["tr"], q_person(four_way(adj["tr"]), person))
            elif neg:
                tr = join(subj["tr"], p_tr, adj["tr"], copula_person("değil", person))
            else:
                tr = join(subj["tr"], p_tr, copula_person(adj["tr"], person))
    else:  # past
        be = en_be(person, pl, past=True)
        en = join(be.capitalize(), lower_subject(subj), adj["en"], p_en, "yesterday") if q else             join(subj["en"], be + ("n't" if neg else ""), adj["en"], p_en, "yesterday")
        if q:
            qp = q_particle(adj["tr"])
            word = qp + "yd" + four_way(qp)
            if person != "3s":
                word = past_person(word, person)
            tr = join(subj["tr"], "dün", p_tr, adj["tr"], word)
        elif neg:
            word = "değildi" if person == "3s" else past_person("değildi", person)
            tr = join(subj["tr"], "dün", p_tr, adj["tr"], word)
        else:
            word = copula_past(adj["tr"])
            tr = join(subj["tr"], "dün", p_tr, word if person == "3s" else past_person(word, person))
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
    if base == "aor":
        out.append("cal_everyday")
    if base == "can":
        out.append("badge_can")
    if form.endswith("_neg"):
        out.append("badge_not")
    if form.endswith("_q"):
        out.append("badge_question")
    return out
