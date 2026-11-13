package com.nexonic.downloadmanager.core

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PowerRulesTest {
    @Test fun chargingAlwaysAllowed() = assertTrue(PowerRules.allowed(true, 15, plugged = true, percent = 5))
    @Test fun chargingOnlyWaitsOnBattery() = assertFalse(PowerRules.allowed(true, 0, plugged = false, percent = 100))
    @Test fun lowBatteryPauses() {
        assertFalse(PowerRules.allowed(false, 15, plugged = false, percent = 14))
        assertTrue(PowerRules.allowed(false, 15, plugged = false, percent = 15))
        assertTrue(PowerRules.allowed(false, 0, plugged = false, percent = 1))
    }
}
