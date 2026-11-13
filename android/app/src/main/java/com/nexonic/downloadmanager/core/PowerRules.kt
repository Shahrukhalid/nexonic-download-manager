package com.nexonic.downloadmanager.core

/** Settings → Only while charging / Pause when the battery is low. */
object PowerRules {
    fun allowed(chargingOnly: Boolean, lowPercent: Int, plugged: Boolean, percent: Int): Boolean {
        if (plugged) return true
        if (chargingOnly) return false
        return lowPercent <= 0 || percent >= lowPercent
    }
}
