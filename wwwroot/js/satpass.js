// Pass prediction using satellite.js (Vallado SGP4/SDP4)
window.predictPasses = function (tles, lat, lon, altKm, startMs, endMs, minElevDeg) {
    var observerGd = {
        longitude: satellite.degreesToRadians(lon),
        latitude:  satellite.degreesToRadians(lat),
        height:    altKm
    };

    var passes = [];
    for (var i = 0; i < tles.length; i++) {
        try {
            var satrec = satellite.twoline2satrec(tles[i].line1, tles[i].line2);
            var sat    = predictSatPasses(satrec, tles[i].noradId, tles[i].satelliteName,
                                          observerGd, startMs, endMs, minElevDeg);
            for (var j = 0; j < sat.length; j++) passes.push(sat[j]);
        } catch (e) { /* skip malformed TLE */ }
    }

    passes.sort(function (a, b) { return a.aosTime - b.aosTime; });
    return passes;
};

function predictSatPasses(satrec, noradId, name, observerGd, startMs, endMs, minElevDeg) {
    var passes  = [];
    var STEP_MS = 15000;
    var R2D     = 180 / Math.PI;

    var inPass = false, aosTime = null, aosAz = 0;
    var maxEl = 0, maxElTime = null, tcaAz = 0;

    for (var t = startMs; t < endMs; t += STEP_MS) {
        var look = getLookAngles(satrec, observerGd, t);
        if (!look) continue;

        var el = look.elevation * R2D;
        var az = ((look.azimuth * R2D) + 360) % 360;

        if (!inPass && el > 0) {
            var aosMs   = refineEvent(satrec, observerGd, t - STEP_MS, t, true);
            var aosLook = getLookAngles(satrec, observerGd, aosMs);
            aosAz    = aosLook ? ((aosLook.azimuth * R2D) + 360) % 360 : az;
            aosTime  = aosMs;
            inPass   = true;
            maxEl    = el;
            maxElTime = t;
            tcaAz    = az;
        } else if (inPass && el > maxEl) {
            maxEl     = el;
            maxElTime = t;
            tcaAz     = az;
        } else if (inPass && el <= 0) {
            var losMs   = refineEvent(satrec, observerGd, t - STEP_MS, t, false);
            if (maxEl >= minElevDeg && aosTime !== null) {
                var losLook = getLookAngles(satrec, observerGd, losMs);
                var losAz   = losLook ? ((losLook.azimuth * R2D) + 360) % 360 : az;
                passes.push({
                    noradId:       noradId,
                    satelliteName: name,
                    aosTime:       Math.round(aosTime),
                    tcaTime:       Math.round(maxElTime),
                    losTime:       Math.round(losMs),
                    maxElevation:  Math.round(maxEl * 10) / 10,
                    aosAzimuth:    aosAz,
                    tcaAzimuth:    tcaAz,
                    losAzimuth:    losAz
                });
            }
            inPass  = false;
            maxEl   = 0;
            aosTime = null;
        }
    }
    return passes;
}

function getLookAngles(satrec, observerGd, ms) {
    try {
        var date = new Date(ms);
        var pv   = satellite.propagate(satrec, date);
        if (!pv || !pv.position || typeof pv.position === 'boolean') return null;
        var gmst = satellite.gstime(date);
        var ecf  = satellite.eciToEcf(pv.position, gmst);
        return satellite.ecfToLookAngles(observerGd, ecf);
    } catch (e) { return null; }
}

function refineEvent(satrec, observerGd, t1Ms, t2Ms, risingEdge) {
    var lo = t1Ms, hi = t2Ms;
    for (var i = 0; i < 8; i++) {
        var mid   = (lo + hi) / 2;
        var look  = getLookAngles(satrec, observerGd, mid);
        var above = look && (look.elevation * (180 / Math.PI)) > 0;
        if (risingEdge) { if (above) hi = mid; else lo = mid; }
        else            { if (above) lo = mid; else hi = mid; }
    }
    return (lo + hi) / 2;
}
