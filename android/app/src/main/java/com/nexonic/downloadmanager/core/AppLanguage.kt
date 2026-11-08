package com.nexonic.downloadmanager.core

import android.app.Activity
import android.app.LocaleManager
import android.content.Context
import android.os.Build
import android.os.LocaleList

/**
 * The language picked in Settings ("" = same as the system). Android 13+ keeps it as the app's own
 * language (also shown in the system's app settings); older versions keep it in the app's settings.
 */
object AppLanguage {
    private fun prefs(c: Context) = c.getSharedPreferences("settings", Context.MODE_PRIVATE)

    fun chosen(c: Context): String =
        if (Build.VERSION.SDK_INT >= 33) {
            val list = c.getSystemService(LocaleManager::class.java).applicationLocales
            if (list.isEmpty) "" else Translations.normalize(list[0].toLanguageTag())
        } else prefs(c).getString("language", "") ?: ""

    /** Loads the strings for the chosen language, or the system's. */
    fun load(c: Context) {
        val code = chosen(c).ifEmpty { c.resources.configuration.locales[0].toLanguageTag() }
        Translations.use(code) { runCatching { c.assets.open(it).bufferedReader().use { r -> r.readText() } }.getOrNull() }
    }

    fun choose(a: Activity, code: String) {
        if (Build.VERSION.SDK_INT >= 33) {
            a.getSystemService(LocaleManager::class.java).applicationLocales =
                if (code.isEmpty()) LocaleList.getEmptyLocaleList() else LocaleList.forLanguageTags(Translations.tag(code))
        } else {
            prefs(a).edit().putString("language", code).apply()
        }
        load(a)
        a.recreate()
    }
}
