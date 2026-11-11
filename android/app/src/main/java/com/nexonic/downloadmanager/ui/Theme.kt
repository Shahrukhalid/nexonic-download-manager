package com.nexonic.downloadmanager.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import com.nexonic.downloadmanager.core.FileCategory

val Brand = Color(0xFF2563EB)
val BrandDark = Color(0xFF60A5FA)
val Success = Color(0xFF16A34A)
val Warning = Color(0xFFD97706)

private val Light = lightColorScheme(
    primary = Brand, onPrimary = Color.White, primaryContainer = Color(0xFFE8EFFF), onPrimaryContainer = Color(0xFF0B2A6F),
    secondary = Color(0xFF475569), background = Color(0xFFF6F8FC), surface = Color.White, surfaceVariant = Color(0xFFEEF2F8),
    onSurfaceVariant = Color(0xFF526077), outline = Color(0xFFD5DCE8),
)
private val Dark = darkColorScheme(
    primary = BrandDark, onPrimary = Color(0xFF0B1220), primaryContainer = Color(0xFF16244A), onPrimaryContainer = Color(0xFFDBE6FF),
    secondary = Color(0xFF9AA7BD), background = Color(0xFF0A1020), surface = Color(0xFF111A30), surfaceVariant = Color(0xFF16213B),
    onSurfaceVariant = Color(0xFF9AA7BD), outline = Color(0xFF26334F),
)

@Composable
fun NexonicTheme(content: @Composable () -> Unit) {
    val scheme = com.nexonic.downloadmanager.core.ColorSchemeChoice.named(Appearance.scheme)
    val dark = scheme?.dark ?: when (Appearance.choice) { "light" -> false; "dark" -> true; else -> isSystemInDarkTheme() }
    val colors = when {
        scheme == null -> if (dark) Dark else Light
        // A colour scheme: its accent, background and panels over the light or dark base.
        dark -> Dark.copy(primary = Color(scheme.accent), onPrimary = Color.Black, background = Color(scheme.background),
            surface = Color(scheme.surface), primaryContainer = Color(scheme.accent).copy(alpha = 0.25f).compositeOver(Color(scheme.surface)),
            onPrimaryContainer = Color.White)
        else -> Light.copy(primary = Color(scheme.accent), onPrimary = Color.White, background = Color(scheme.background),
            surface = Color(scheme.surface), primaryContainer = Color(scheme.accent).copy(alpha = 0.15f).compositeOver(Color.White),
            onPrimaryContainer = Color.Black)
    }
    MaterialTheme(colorScheme = colors, content = content)
}

/** Settings → Appearance: the same as the phone, light or dark, and a colour scheme (changes right away). */
object Appearance {
    var choice by androidx.compose.runtime.mutableStateOf("system")
    var scheme by androidx.compose.runtime.mutableStateOf("")
}

fun categoryColor(c: FileCategory): Color = when (c) {
    FileCategory.VIDEO -> Color(0xFF7C3AED)
    FileCategory.MUSIC -> Color(0xFFDB2777)
    FileCategory.COMPRESSED -> Color(0xFFD97706)
    FileCategory.DOCUMENTS -> Color(0xFF2563EB)
    FileCategory.PROGRAMS -> Color(0xFF059669)
    FileCategory.IMAGES -> Color(0xFF0891B2)
    FileCategory.OTHER -> Color(0xFF64748B)
}
