@file:OptIn(ExperimentalMaterial3Api::class)

package com.nexonic.downloadmanager.ui

import com.nexonic.downloadmanager.video.VideoEngine

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings as SystemSettings
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.nexonic.downloadmanager.BuildConfig
import com.nexonic.downloadmanager.browser.Browser
import com.nexonic.downloadmanager.data.Downloads
import com.nexonic.downloadmanager.service.Scheduler

@Composable
fun SettingsScreen(browser: Browser, message: (String) -> Unit) {
    val s = Downloads.settings
    val context = LocalContext.current
    var connections by remember { mutableFloatStateOf(s.connections.toFloat()) }
    var maxActive by remember { mutableFloatStateOf(s.maxActive.toFloat()) }
    var limit by remember { mutableStateOf(if (s.speedLimitKB > 0) s.speedLimitKB.toString() else "") }
    var wifiOnly by remember { mutableStateOf(s.wifiOnly) }
    var ask by remember { mutableStateOf(s.askBeforeDownload) }
    var folders by remember { mutableStateOf(s.categoryFolders) }
    var schedule by remember { mutableStateOf(s.scheduleEnabled) }
    var minutes by remember { mutableStateOf(s.scheduleMinutes) }
    var pickTime by remember { mutableStateOf(false) }
    var block3p by remember { mutableStateOf(s.blockThirdPartyCookies) }
    var home by remember { mutableStateOf(s.homePage) }
    var confirmClear by remember { mutableStateOf(false) }
    var engineVersion by remember { mutableStateOf<String?>(null) }
    var updating by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { engineVersion = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { VideoEngine.version() } }

    Column(Modifier.fillMaxSize()) {
        TopAppBar(title = { Text("Settings", fontWeight = FontWeight.SemiBold) })
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp)) {
            Section("Downloads")
            Text("Connections per download: ${connections.toInt()}", style = MaterialTheme.typography.bodyLarge)
            Slider(connections, { connections = it }, valueRange = 1f..32f, steps = 30, onValueChangeFinished = { s.connections = connections.toInt() })
            Hint("More connections are faster on most servers. Each part is at least 512 KB.")
            Spacer(Modifier.height(8.dp))
            Text("Downloads at the same time: ${maxActive.toInt()}", style = MaterialTheme.typography.bodyLarge)
            Slider(maxActive, { maxActive = it }, valueRange = 1f..6f, steps = 4, onValueChangeFinished = { s.maxActive = maxActive.toInt(); Downloads.pump() })
            OutlinedTextField(
                value = limit,
                onValueChange = { v -> limit = v.filter { it.isDigit() }.take(7); Downloads.setSpeedLimit(limit.toLongOrNull() ?: 0) },
                label = { Text("Speed limit (KB/s, empty = no limit)") },
                singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
            )
            Toggle("Wi-Fi only", "Wait for Wi-Fi or another unmetered network before downloading.", wifiOnly) {
                wifiOnly = it; s.wifiOnly = it; Downloads.pump()
            }
            Toggle("Ask before downloading", "Show the download window when the browser starts a download.", ask) { ask = it; s.askBeforeDownload = it }
            Toggle("Folders by type", "Save into Downloads/Nexonic/Video, /Music, /Documents…", folders) { folders = it; s.categoryFolders = it }

            Section("Scheduler")
            Toggle("Start downloads kept for later", "Every day at ${"%02d:%02d".format(minutes / 60, minutes % 60)}", schedule) {
                schedule = it; s.scheduleEnabled = it; Scheduler.apply(context)
            }
            if (schedule) {
                TextButton(onClick = { pickTime = true }) { Text("Change time") }
                if (!Scheduler.canExact(context) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    Hint("Android may delay scheduled downloads or ask you to tap a notification. Allow alarms for exact timing.")
                    TextButton(onClick = {
                        runCatching { context.startActivity(Intent(SystemSettings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:${context.packageName}"))) }
                    }) { Text("Allow alarms") }
                }
            }

            Section("Browser and privacy")
            Toggle("Block third-party cookies", "Sites can't track you across other sites.", block3p) {
                block3p = it; s.blockThirdPartyCookies = it; browser.applyPrivacy()
            }
            OutlinedTextField(home, { home = it; if (com.nexonic.downloadmanager.core.Links.isWebLink(it)) s.homePage = it },
                label = { Text("Home page") }, singleLine = true, modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp))
            TextButton(onClick = { browser.clearData(); message("Browser data cleared") }) { Text("Clear cookies, cache and history") }
            TextButton(onClick = { confirmClear = true }) { Text("Clear finished downloads from the list") }
            Hint("No account, no ads, no tracking. Cookies and passwords for downloads are encrypted on this phone and sent only to their own site.")

            Section("Video engine")
            Hint("Saves videos from YouTube, Facebook, Instagram, TikTok, X and 1,000+ other sites (yt-dlp with ffmpeg, like the desktop apps). It updates itself once a day.")
            Text("Version: " + (engineVersion ?: "starting…"), style = MaterialTheme.typography.bodyMedium)
            TextButton(enabled = !updating, onClick = {
                updating = true
                Thread {
                    val result = runCatching { VideoEngine.updateNow() }
                    android.os.Handler(android.os.Looper.getMainLooper()).post {
                        updating = false
                        engineVersion = VideoEngine.version()
                        message(if (result.isSuccess) (if (result.getOrNull() == "ALREADY_UP_TO_DATE") "The video engine is up to date" else "Video engine updated") else "Could not update now. Try again later.")
                    }
                }.start()
            }) { Text(if (updating) "Checking…" else "Update video engine") }

            Section("About")
            Text("Nexonic Download Manager ${BuildConfig.VERSION_NAME}", style = MaterialTheme.typography.bodyLarge)
            Hint("Free and open source (MIT) by Shahrukh Khalid.")
            TextButton(onClick = { openLink(context, "https://shahrukhalid.github.io/nexonic-download-manager/") }) { Text("Website") }
            TextButton(onClick = { openLink(context, "https://github.com/Shahrukhalid/nexonic-download-manager") }) { Text("Source code and help") }
            TextButton(onClick = { openLink(context, "https://github.com/Shahrukhalid/nexonic-download-manager/issues/new") }) { Text("Report a problem") }
            Hint("Only download what you have the right to save. Copy-protected services are not supported.")
            Spacer(Modifier.height(32.dp))
        }
    }

    if (pickTime) {
        val state = rememberTimePickerState(initialHour = minutes / 60, initialMinute = minutes % 60)
        AlertDialog(
            onDismissRequest = { pickTime = false },
            title = { Text("Start at") },
            text = { TimePicker(state) },
            confirmButton = {
                TextButton(onClick = {
                    minutes = state.hour * 60 + state.minute; s.scheduleMinutes = minutes; Scheduler.apply(context); pickTime = false
                }) { Text("Save") }
            },
            dismissButton = { TextButton(onClick = { pickTime = false }) { Text("Cancel") } },
        )
    }
    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text("Clear finished downloads?") },
            text = { Text("They are removed from the list. The files stay in your Downloads folder.") },
            confirmButton = { TextButton(onClick = { Downloads.clearFinished(); confirmClear = false; message("List cleared") }) { Text("Clear") } },
            dismissButton = { TextButton(onClick = { confirmClear = false }) { Text("Cancel") } },
        )
    }
}

private fun openLink(context: android.content.Context, url: String) {
    runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
}

@Composable
private fun Section(title: String) {
    Spacer(Modifier.height(20.dp))
    Text(title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.SemiBold)
    HorizontalDivider(Modifier.padding(top = 6.dp, bottom = 10.dp))
}

@Composable
private fun Hint(text: String) {
    Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
private fun Toggle(title: String, subtitle: String, value: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { onChange(!value) }.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge)
            Hint(subtitle)
        }
        Switch(value, onChange)
    }
}
