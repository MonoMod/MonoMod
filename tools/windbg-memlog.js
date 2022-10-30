"use strict";

function initializeScript()
{
    //
    // Return an array of registration objects to modify the object model of the debugger
    // See the following for more details:
    //
    //     https://aka.ms/JsDbgExt
    //
    return [new host.apiVersionSupport(1, 7)];
}

function GetMMLog()
{
    var ctl = host.namespace.Debugger.Utility.Control;
    var getDbgLog1 = ctl.ExecuteCommand("!sos.name2ee MonoMod.Utils.dll MonoMod.Logs.DebugLog");

    var eeclsPtr;
    for (var line of getDbgLog1) {
        var eeclsi = line.indexOf("EEClass:");
        if (eeclsi >= 0) {
            eeclsPtr = line.slice(eeclsi + 9).trim();
            break;
        }
    }

    var getDbgLog2 = ctl.ExecuteCommand("!sos.dumpclass " + eeclsPtr);

    var memlogPtr;
    var memlogPos;
    for (var line of getDbgLog2) {
        var memlogPosi = line.indexOf("memlogPos");
        if (memlogPosi >= 0) {
            var statici = line.indexOf("static");
            var ptr1 = line.slice(0, memlogPosi).slice(statici + 6);
            ptr1 = ptr1.trim();
            memlogPos = host.parseInt64(ptr1, 10);
            continue;
        }
        var memlogi = line.indexOf("memlog");
        if (memlogi >= 0) {
            var statici = line.indexOf("static");
            var ptr1 = line.slice(0, memlogi).slice(statici + 6);
            ptr1 = ptr1.trim();
            memlogPtr = host.parseInt64(ptr1, 16);
            continue;
        }
    }

    if (memlogPtr == 0) {
        return "Memory log is not enabled"
    }

    // for now, we assume 64-bit
    var memlogMaxLen = host.memory.readMemoryValues(memlogPtr.add(8), 1, 8)[0];
    var memlogBase = memlogPtr.add(16);
    var memlogEnd = memlogBase.add(memlogPos);

    class iter {
        *[Symbol.iterator]() {
            var i = 0;
            var msgBase = memlogBase;
            while (msgBase < memlogEnd) {
                // read the message
                var level = host.memory.readMemoryValues(msgBase, 1, 1)[0];
                msgBase = msgBase.add(1);
                var timestamp = host.memory.readMemoryValues(msgBase, 1, 8)[0];
                msgBase = msgBase.add(8);
                var srcLen = host.memory.readMemoryValues(msgBase, 1, 1)[0];
                msgBase = msgBase.add(1);
                var src = host.memory.readWideString(msgBase, srcLen);
                msgBase = msgBase.add(srcLen * 2);
                var msgLen = host.memory.readMemoryValues(msgBase, 1, 4)[0];
                msgBase = msgBase.add(4);
                var msg = host.memory.readWideString(msgBase, msgLen);
                msgBase = msgBase.add(msgLen * 2);

                yield { Index: i++, Level: formatLevel(level), Time: formatDateTime(timestamp), Source: src, Message: msg };
            }
        }
    }

    return new iter();
}

function formatLevel(num) {
    var names = ["Spam", "Trace", "Info", "Warning", "Error", "Assert"];
    if (num >= names.length) {
        return num.toString();
    }
    return names[num];
}

function formatDateTime(dt) {
    // remove UTC and flags
    var m1 = host.parseInt64("ffffffffffffffff", 16);

    var utcBit = dt.bitwiseAnd(m1.bitwiseShiftLeft(63).bitwiseShiftRight(1));
    var isUtc = utcBit == 0;

    var ticks = dt.bitwiseAnd(m1.bitwiseShiftRight(2));

    var ticksPerDay = host.parseInt64("864000000000");
    var ticksPerHour = host.parseInt64("36000000000");
    var ticksPerMinute = host.parseInt64("600000000");
    var ticksPerSecond = host.parseInt64("10000000");
    var ticksPerMillis = host.parseInt64("10000");

    var numDays = ticks.divide(ticksPerDay);
    var hours = ticks.subtract(numDays.multiply(ticksPerDay)).divide(ticksPerHour);
    var minutes = ticks
                .subtract(numDays.multiply(ticksPerDay))
                .subtract(hours.multiply(ticksPerHour)).divide(ticksPerMinute);
    var seconds = ticks
                .subtract(numDays.multiply(ticksPerDay))
                .subtract(hours.multiply(ticksPerHour)) 
                .subtract(minutes.multiply(ticksPerMinute)).divide(ticksPerSecond);
    var millis = ticks
                .subtract(numDays.multiply(ticksPerDay))
                .subtract(hours.multiply(ticksPerHour)) 
                .subtract(minutes.multiply(ticksPerMinute))
                .subtract(seconds.multiply(ticksPerSecond)).divide(ticksPerMillis);

    // DaysPerYear = 365
    // DaysPer4Years = DaysPerYear * 4 + 1 == 1461
    // DaysPer100Years = DaysPer4Years * 25 - 1 == 36524
    // DaysPer400Years = DaysPer100Years * 4 + 1 == 146097

    var n400yr = numDays.divide(146097);
    numDays = numDays.subtract(n400yr.multiply(146097));
    var n100yr = numDays.divide(36524);
    numDays = numDays.subtract(n100yr.multiply(36524));
    var n4yr = numDays.divide(1461);
    numDays = numDays.subtract(n4yr.multiply(1461));
    var n1yr = numDays.divide(365);
    if (n1yr == 4) { n1yr = 3; }

    var dayInYr = numDays - (n1yr * 365);

    var year = (n400yr * 400) + (n100yr * 100) + (n4yr * 4) + n1yr + 1;

    // compute leap year
    var isLeap = false;
    if (n1yr == 3) {
        if (n4yr != 24) {
            isLeap = true;
        }
        if (n100yr == 3) {
            isLeap - true;
        }
    }

    // month and day
    var month = 0;
    var day = 0;
    if (!isLeap) {
        if (dayInYr <= 31) {
            month = 1;
            day = dayInYr - 0 + 1;
        } else if (dayInYr <= 59) {
            month = 2;
            day = dayInYr - 31 + 1;
        } else if (dayInYr <= 90) {
            month = 3;
            day = dayInYr - 59 + 1;
        } else if (dayInYr <= 120) {
            month = 4;
            day = dayInYr - 90 + 1;
        } else if (dayInYr <= 151) {
            month = 5;
            day = dayInYr - 120 + 1;
        } else if (dayInYr <= 181) {
            month = 6;
            day = dayInYr - 151 + 1;
        } else if (dayInYr <= 212) {
            month = 7;
            day = dayInYr - 181 + 1;
        } else if (dayInYr <= 243) {
            month = 8;
            day = dayInYr - 212 + 1;
        } else if (dayInYr <= 273) {
            month = 9;
            day = dayInYr - 243 + 1;
        } else if (dayInYr <= 304) {
            month = 10;
            day = dayInYr - 273 + 1;
        } else if (dayInYr <= 334) {
            month = 11;
            day = dayInYr - 304 + 1;
        } else {
            month = 12;
            day = dayInYr - 334 + 1;
        }
    } else {
        if (dayInYr <= 31) {
            month = 1;
            day = dayInYr - 0 + 1;
        } else if (dayInYr <= 60) {
            month = 2;
            day = dayInYr - 31 + 1;
        } else if (dayInYr <= 91) {
            month = 3;
            day = dayInYr - 60 + 1;
        } else if (dayInYr <= 121) {
            month = 4;
            day = dayInYr - 91 + 1;
        } else if (dayInYr <= 152) {
            month = 5;
            day = dayInYr - 121 + 1;
        } else if (dayInYr <= 182) {
            month = 6;
            day = dayInYr - 152 + 1;
        } else if (dayInYr <= 213) {
            month = 7;
            day = dayInYr - 182 + 1;
        } else if (dayInYr <= 244) {
            month = 8;
            day = dayInYr - 213 + 1;
        } else if (dayInYr <= 274) {
            month = 9;
            day = dayInYr - 244 + 1;
        } else if (dayInYr <= 305) {
            month = 10;
            day = dayInYr - 274 + 1;
        } else if (dayInYr <= 335) {
            month = 11;
            day = dayInYr - 305 + 1;
        } else {
            month = 12;
            day = dayInYr - 335 + 1;
        }
    }

    var date = isUtc
        ? new Date(Date.UTC(year, month - 1, day, hours, minutes, seconds, millis))
        : new Date(year, month - 1, day, hours, minutes, seconds, millis);
    return date.toLocaleString();
}