@file:OptIn(ExperimentalMaterial3Api::class)

package com.nexonic.downloadmanager.ui

import com.nexonic.downloadmanager.video.VideoEngine
import com.nexonic.downloadmanager.core.tr

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
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.horizontalScroll
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
    var subtitles by remember { mutableStateOf(s.videoSubtitles) }
    var schedule by remember { mutableStateOf(s.scheduleEnabled) }
    var minutes by remember { mutableStateOf(s.scheduleMinutes) }
    var pickTime by remember { mutableStateOf(false) }
    var showLicenses by remember { mutableStateOf(false) }
    var block3p by remember { mutableStateOf(s.blockThirdPartyCookies) }
    var home by remember { mutableStateOf(s.homePage) }
    var engine by remember { mutableStateOf(s.searchEngine) }
    var pickEngine by remember { mutableStateOf(false) }
    var pickLanguage by remember { mutableStateOf(false) }
    val language = remember { com.nexonic.downloadmanager.core.AppLanguage.chosen(context) }
    var notifyDone by remember { mutableStateOf(s.notifyFinished) }
    var notifyFail by remember { mutableStateOf(s.notifyFailed) }
    var treeName by remember { mutableStateOf(if (s.saveTree != null) s.saveTreeName else "") }
    val pickFolder = androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri != null) {
            val flags = Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            val kept = runCatching { context.contentResolver.takePersistableUriPermission(uri, flags) }.isSuccess
            if (kept) {
                s.saveTree?.let { old -> if (old != uri.toString()) runCatching { context.contentResolver.releasePersistableUriPermission(Uri.parse(old), flags) } }
                val name = Uri.decode(uri.lastPathSegment.orEmpty()).substringAfterLast(':').ifEmpty { "Chosen folder" }
                val volume = Uri.decode(uri.lastPathSegment.orEmpty()).substringBefore(':')
                s.saveTree = uri.toString()
                s.saveTreeName = if (volume == "primary") name else "$name (SD card or USB)"
                treeName = s.saveTreeName
            } else message("This folder can't be used. Choose another one.")
        }
    }
    var confirmClear by remember { mutableStateOf(false) }
    var engineVersion by remember { mutableStateOf<String?>(null) }
    var updating by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { engineVersion = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { VideoEngine.version() } }

    Column(Modifier.fillMaxSize()) {
        TopAppBar(title = { Text(tr("Settings"), fontWeight = FontWeight.SemiBold) })
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp)) {
            Section("Language")
            val languageName = if (language.isEmpty()) tr("Same as the system") else com.nexonic.downloadmanager.core.Translations.names[language] ?: language
            TextButton(onClick = { pickLanguage = true }) { Text(languageName) }
            if (pickLanguage) AlertDialog(
                onDismissRequest = { pickLanguage = false },
                title = { Text(tr("Language")) },
                text = {
                    Column(Modifier.verticalScroll(rememberScrollState())) {
                        (listOf("", "en") + com.nexonic.downloadmanager.core.Translations.languages).forEach { code ->
                            val pick = {
                                pickLanguage = false
                                if (code != language) (context as? android.app.Activity)?.let { com.nexonic.downloadmanager.core.AppLanguage.choose(it, code) }
                            }
                            Row(Modifier.fillMaxWidth().clickable(onClick = pick).padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                                androidx.compose.material3.RadioButton(selected = code == language, onClick = pick)
                                Text(if (code.isEmpty()) tr("Same as the system") else com.nexonic.downloadmanager.core.Translations.names[code] ?: code)
                            }
                        }
                    }
                },
                confirmButton = { TextButton(onClick = { pickLanguage = false }) { Text(tr("Close")) } },
            )

            Section("Appearance")
            var look by remember { mutableStateOf(s.appearance) }
            Row(verticalAlignment = Alignment.CenterVertically) {
                listOf("system" to "Same as the phone", "light" to "Light", "dark" to "Dark").forEach { (code, title) ->
                    val pick = { look = code; s.appearance = code; Appearance.choice = code }
                    androidx.compose.material3.RadioButton(selected = look == code, onClick = pick)
                    Text(tr(title), Modifier.clickable(onClick = pick).padding(end = 12.dp))
                }
            }
            var scheme by remember { mutableStateOf(s.colorScheme) }
            Text(tr("Colour scheme"), style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp))
            Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
                val own = com.nexonic.downloadmanager.core.ColorSchemeChoice
                val ownCode = if (own.custom(scheme) != null) scheme else own.customCode(false, "#2563EB", "#F6F8FC")
                (listOf("" to "None") + own.builtIn.map { it.name to it.name } + listOf(ownCode to "My own")).forEach { (code, title) ->
                    val pick = { scheme = code; s.colorScheme = code; Appearance.scheme = code }
                    androidx.compose.material3.RadioButton(selected = scheme == code, onClick = pick)
                    Text(tr(title), Modifier.clickable(onClick = pick).padding(end = 12.dp))
                }
            }
            com.nexonic.downloadmanager.core.ColorSchemeChoice.custom(scheme)?.let { mine ->
                val parts = scheme.split(':')
                var accentText by remember(scheme) { mutableStateOf(parts[2]) }
                var backgroundText by remember(scheme) { mutableStateOf(parts[3]) }
                fun save(dark: Boolean = mine.dark, accent: String = accentText, background: String = backgroundText) {
                    val code = com.nexonic.downloadmanager.core.ColorSchemeChoice.customCode(dark, accent, background)
                    if (com.nexonic.downloadmanager.core.ColorSchemeChoice.custom(code) != null) { scheme = code; s.colorScheme = code; Appearance.scheme = code }
                }
                Toggle("Dark", "Light or dark text and panels for your scheme.", mine.dark) { save(dark = it) }
                Row {
                    OutlinedTextField(accentText, { accentText = it; save(accent = it) }, Modifier.weight(1f).padding(end = 6.dp), singleLine = true,
                        label = { Text(tr("Accent (#RRGGBB)")) }, isError = com.nexonic.downloadmanager.core.ColorSchemeChoice.rgb(accentText) == null)
                    OutlinedTextField(backgroundText, { backgroundText = it; save(background = it) }, Modifier.weight(1f), singleLine = true,
                        label = { Text(tr("Background (#RRGGBB)")) }, isError = com.nexonic.downloadmanager.core.ColorSchemeChoice.rgb(backgroundText) == null)
                }
                val ratio = com.nexonic.downloadmanager.core.ColorSchemeChoice.contrast(mine.accent, mine.background)
                if (ratio < 3) Hint(tr("The accent is hard to see on this background (contrast %.1f:1, at least 3:1 is needed).").format(ratio))
            }
            var font by remember { mutableStateOf(s.appFont) }
            Choices("Font", listOf("system" to "Same as the phone", "sans" to "Sans serif", "serif" to "Serif", "mono" to "Monospace"), font) {
                font = it; s.appFont = it; Appearance.font = it
            }
            var textScale by remember { mutableStateOf(s.textScale.toString()) }
            Choices("Text size", listOf("85", "100", "115", "130", "150").map { it to "$it%" }, textScale) {
                textScale = it; s.textScale = it.toInt(); Appearance.textScale = it.toInt()
            }
            var sizeUnits by remember { mutableStateOf(s.sizeUnits) }
            Choices("Sizes", listOf("binary" to "KB, MB (1 KB = 1024 bytes)", "iec" to "KiB, MiB", "decimal" to "KB, MB (1 KB = 1000 bytes)"), sizeUnits) {
                sizeUnits = it; s.sizeUnits = it; s.applyFormats(); Appearance.units++
            }
            var speedUnits by remember { mutableStateOf(s.speedUnits) }
            Choices("Speeds", listOf("bytes" to "MB/s", "bits" to "Mbit/s"), speedUnits) {
                speedUnits = it; s.speedUnits = it; s.applyFormats(); Appearance.units++
            }
            var dateStyle by remember { mutableStateOf(s.dateStyle) }
            Choices("Dates", listOf("absolute" to "Date and time", "relative" to "How long ago (5 min ago)"), dateStyle) {
                dateStyle = it; s.dateStyle = it; s.applyFormats(); Appearance.units++
            }

            Section("Debrid service (optional)")
            var debrid by remember { mutableStateOf(s.debridService) }
            Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
                com.nexonic.downloadmanager.core.DebridService.entries.forEach { d ->
                    val pick = { debrid = d.name; s.debridService = d.name }
                    androidx.compose.material3.RadioButton(selected = debrid == d.name, onClick = pick)
                    Text(tr(d.title), Modifier.clickable(onClick = pick).padding(end = 12.dp))
                }
            }
            if (debrid != "NONE") {
                var key by remember { mutableStateOf(Downloads.debridKey) }
                OutlinedTextField(key, { key = it.take(500); Downloads.debridKey = key }, label = { Text(tr("Your API key for the service")) },
                    singleLine = true, visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp))
                var hosts by remember { mutableStateOf(s.debridHosts) }
                OutlinedTextField(hosts, { hosts = it.take(2000); s.debridHosts = hosts },
                    label = { Text(tr("Sites whose links go through it (comma separated, e.g. example-host.com)")) },
                    modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp))
            }
            Hint("Off by default. Uses your own account; links from other sites are never sent to the service. The key is saved encrypted and never in backups.")

            Section("Downloads")
            var auto by remember { mutableStateOf(s.autoConnections) }
            Toggle("Automatic connections", "Starts with a few and adds more while the speed keeps rising; slows down when a server asks.", auto) { auto = it; s.autoConnections = it }
            Text((if (auto) "Most connections per download: " else "Connections per download: ") + connections.toInt(), style = MaterialTheme.typography.bodyLarge)
            Slider(connections, { connections = it }, valueRange = 1f..256f, steps = 254, onValueChangeFinished = { s.connections = connections.toInt() })
            Hint("More connections are faster on most servers. Each part is at least 512 KB.")
            Spacer(Modifier.height(8.dp))
            Text("Downloads at the same time: ${maxActive.toInt()}", style = MaterialTheme.typography.bodyLarge)
            Slider(maxActive, { maxActive = it }, valueRange = 1f..6f, steps = 4, onValueChangeFinished = { s.maxActive = maxActive.toInt(); Downloads.pump() })
            OutlinedTextField(
                value = limit,
                onValueChange = { v -> limit = v.filter { it.isDigit() }.take(7); Downloads.setSpeedLimit(limit.toLongOrNull() ?: 0) },
                label = { Text(tr("Speed limit (KB/s, empty = no limit)")) },
                singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
            )
            var speedSchedule by remember { mutableStateOf(s.speedSchedule) }
            val scheduleProblem = remember(speedSchedule) { com.nexonic.downloadmanager.core.SpeedSchedule.parse(speedSchedule).problem }
            OutlinedTextField(
                value = speedSchedule,
                onValueChange = { v ->
                    speedSchedule = v
                    if (com.nexonic.downloadmanager.core.SpeedSchedule.parse(v).problem == null) { s.speedSchedule = v; Downloads.applySpeedLimit() }
                },
                label = { Text(tr("Speed schedule (one rule per line)")) },
                placeholder = { Text("Mon-Fri 09:00-17:00 300") },
                isError = scheduleProblem != null,
                modifier = Modifier.fillMaxWidth().heightIn(min = 80.dp),
            )
            Hint(scheduleProblem ?: "Days, start-end time and KB/s (0 = no limit), for example weekends 23:00-07:00 0. The first rule that applies wins; at other times the speed limit above applies.")
            Toggle("Wi-Fi only", "Wait for Wi-Fi or another unmetered network before downloading.", wifiOnly) {
                wifiOnly = it; s.wifiOnly = it; Downloads.pump()
            }
            var chargingOnly by remember { mutableStateOf(s.chargingOnly) }
            var lowBattery by remember { mutableStateOf(s.lowBatteryPercent > 0) }
            Toggle("Only while charging", "Downloads wait until the phone is on a charger, and pause when it is unplugged.", chargingOnly) {
                chargingOnly = it; Downloads.setPowerRules(it, s.lowBatteryPercent)
            }
            Toggle("Pause when the battery is low", "Below 15% and not charging, downloads pause and continue once the phone charges.", lowBattery) {
                lowBattery = it; Downloads.setPowerRules(s.chargingOnly, if (it) 15 else 0)
            }
            var capMB by remember { mutableStateOf(if (s.dataCapMB > 0) s.dataCapMB.toString() else "") }
            var capHours by remember { mutableStateOf(s.dataCapHours.toString()) }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = capMB,
                    onValueChange = { v -> capMB = v.filter { it.isDigit() }.take(7); s.dataCapMB = capMB.toIntOrNull() ?: 0; Downloads.releaseCap() },
                    label = { Text(tr("Download limit (MB, empty = none)")) },
                    singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.weight(1f).padding(end = 8.dp),
                )
                OutlinedTextField(
                    value = capHours,
                    onValueChange = { v -> capHours = v.filter { it.isDigit() }.take(3); capHours.toIntOrNull()?.let { s.dataCapHours = it } },
                    label = { Text(tr("every hours")) },
                    singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.width(120.dp),
                )
            }
            Hint("When the amount is reached, downloads stop and continue by themselves when the next period starts. Useful on mobile data plans.")
            var blocklist by remember { mutableStateOf(s.torrentBlocklist) }
            OutlinedTextField(
                value = blocklist,
                onValueChange = { blocklist = it.take(2000); if (com.nexonic.downloadmanager.core.TorrentBlocklist.problem(blocklist) == null) s.torrentBlocklist = blocklist },
                label = { Text(tr("Torrent blocklist: an https:// address (empty = none)")) },
                isError = com.nexonic.downloadmanager.core.TorrentBlocklist.problem(blocklist) != null,
                supportingText = { Text(tr(com.nexonic.downloadmanager.core.TorrentBlocklist.problem(blocklist) ?: "Torrents never connect to the addresses in this list. P2P, DAT and CIDR lists work, also as .gz. An address is downloaded again once a day.")) },
                singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
            )
            var vpnOnly by remember { mutableStateOf(s.vpnOnly) }
            Toggle("VPN only", "Download only through a VPN. If the VPN disconnects, downloads stop instead of using another connection.", vpnOnly) {
                vpnOnly = it; Downloads.setVpnOnly(it)
            }
            Toggle("Ask before downloading", "Show the download window when the browser starts a download.", ask) { ask = it; s.askBeforeDownload = it }
            Toggle("Folders by type", "Save into Nexonic/Video, /Music, /Documents…", folders) { folders = it; s.categoryFolders = it }
            // Default browser: links from other apps open here (pages in the built-in browser, files as downloads).
            val roles = remember { context.getSystemService(android.app.role.RoleManager::class.java) }
            var isDefault by remember { mutableStateOf(roles?.isRoleHeld(android.app.role.RoleManager.ROLE_BROWSER) == true) }
            val askRole = androidx.activity.compose.rememberLauncherForActivityResult(
                androidx.activity.result.contract.ActivityResultContracts.StartActivityForResult()
            ) { isDefault = roles?.isRoleHeld(android.app.role.RoleManager.ROLE_BROWSER) == true }
            Text(
                tr(if (isDefault) "Nexonic opens your links (it is the default browser)." else "Open links from other apps with Nexonic"),
                style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp),
            )
            TextButton(onClick = {
                val available = roles?.isRoleAvailable(android.app.role.RoleManager.ROLE_BROWSER) == true
                if (available && !isDefault) askRole.launch(roles!!.createRequestRoleIntent(android.app.role.RoleManager.ROLE_BROWSER))
                else runCatching { context.startActivity(Intent(android.provider.Settings.ACTION_MANAGE_DEFAULT_APPS_SETTINGS)) }
            }) { Text(tr(if (isDefault) "Change the default browser" else "Make Nexonic the default browser")) }
            CategoryList()
            Toggle("Save subtitles with videos", "Subtitles in your phone's language and English, inside the video file.", subtitles) { subtitles = it; s.videoSubtitles = it }
            var audioFormat by remember { mutableStateOf(s.audioFormat) }
            Choices("Sound-only downloads", listOf("original" to "Original format", "mp3" to "MP3", "opus" to "Opus", "flac" to "FLAC"), audioFormat) {
                audioFormat = it; s.audioFormat = it
            }
            Hint("Converting uses the built-in FFmpeg. MP3 uses the best variable quality; FLAC keeps every detail of the sound the site sends.")
            var manualActive by remember { mutableStateOf(s.maxManualActive.toString()) }
            Choices("Downloads I resume myself, beside the queue", listOf("0" to "Off", "1" to "1", "2" to "2", "3" to "3"), manualActive) {
                manualActive = it; s.maxManualActive = it.toInt()
            }
            Hint("Off: a resumed download waits for its turn like the others.")
            var removeMissing by remember { mutableStateOf(s.removeMissingOnStart) }
            Toggle("Remove missing files from the list", "When the app starts, finished downloads whose file was deleted or moved leave the list.", removeMissing) {
                removeMissing = it; s.removeMissingOnStart = it
            }
            TextButton(onClick = {
                Thread {
                    val n = Downloads.removeMissing()
                    android.os.Handler(android.os.Looper.getMainLooper()).post { message(tr("Removed %d from the list. Files on the phone are not touched.").format(n)) }
                }.start()
            }) { Text(tr("Remove Missing Files Now")) }
            Text(tr("Save to"), style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp))
            Hint(if (treeName.isEmpty()) "Downloads/Nexonic on this phone" else treeName)
            Row {
                TextButton(onClick = { pickFolder.launch(null) }) { Text(tr("Choose folder (SD card, USB…)")) }
                if (treeName.isNotEmpty()) TextButton(onClick = {
                    s.saveTree?.let { old -> runCatching { context.contentResolver.releasePersistableUriPermission(Uri.parse(old), Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION) } }
                    s.saveTree = null; s.saveTreeName = ""; treeName = ""
                }) { Text(tr("Use Downloads")) }
            }

            Section("Notifications")
            Toggle("When a download finishes", "A notification you can tap to open the file.", notifyDone) { notifyDone = it; s.notifyFinished = it }
            Toggle("When a download fails", "Says what went wrong.", notifyFail) { notifyFail = it; s.notifyFailed = it }
            var soundDone by remember { mutableStateOf(s.soundFinished) }
            var soundFail by remember { mutableStateOf(s.soundFailed) }
            var pickingFor by remember { mutableStateOf("") }
            val pickSound = androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.StartActivityForResult()) { r ->
                if (r.resultCode != android.app.Activity.RESULT_OK) return@rememberLauncherForActivityResult
                @Suppress("DEPRECATION")
                val uri = r.data?.getParcelableExtra<Uri>(android.media.RingtoneManager.EXTRA_RINGTONE_PICKED_URI)
                val value = uri?.toString() ?: "none"
                if (pickingFor == "done") { soundDone = value; s.soundFinished = value } else { soundFail = value; s.soundFailed = value }
            }
            fun soundName(v: String): String = when (v) {
                "" -> tr("Phone's default")
                "none" -> tr("Silent")
                else -> runCatching { android.media.RingtoneManager.getRingtone(context, Uri.parse(v))?.getTitle(context) }.getOrNull() ?: tr("Chosen sound")
            }
            fun choose(forWhat: String, current: String) {
                pickingFor = forWhat
                val i = Intent(android.media.RingtoneManager.ACTION_RINGTONE_PICKER)
                    .putExtra(android.media.RingtoneManager.EXTRA_RINGTONE_TYPE, android.media.RingtoneManager.TYPE_NOTIFICATION)
                    .putExtra(android.media.RingtoneManager.EXTRA_RINGTONE_SHOW_SILENT, true)
                    .putExtra(android.media.RingtoneManager.EXTRA_RINGTONE_SHOW_DEFAULT, true)
                    .putExtra(android.media.RingtoneManager.EXTRA_RINGTONE_EXISTING_URI,
                        when (current) { "" -> android.provider.Settings.System.DEFAULT_NOTIFICATION_URI; "none" -> null; else -> Uri.parse(current) })
                runCatching { pickSound.launch(i) }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(tr("Sound when finished: ") + soundName(soundDone), Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
                TextButton(onClick = { choose("done", soundDone) }) { Text(tr("Change")) }
                if (soundDone.isNotEmpty()) TextButton(onClick = { soundDone = ""; s.soundFinished = "" }) { Text(tr("Default")) }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(tr("Sound when failed: ") + soundName(soundFail), Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
                TextButton(onClick = { choose("fail", soundFail) }) { Text(tr("Change")) }
                if (soundFail.isNotEmpty()) TextButton(onClick = { soundFail = ""; s.soundFailed = "" }) { Text(tr("Default")) }
            }
            var vibrate by remember { mutableStateOf(s.notifyVibrate) }
            Toggle("Vibrate", "With the finished and failed notifications.", vibrate) { vibrate = it; s.notifyVibrate = it }
            var quietHours by remember { mutableStateOf(s.quietHours) }
            OutlinedTextField(
                value = quietHours,
                onValueChange = { quietHours = it; if (it.isBlank() || com.nexonic.downloadmanager.core.QuietHours.parse(it) != null) s.quietHours = it },
                label = { Text(tr("Quiet hours, no sound (for example 22:00-07:00)")) },
                isError = quietHours.isNotBlank() && com.nexonic.downloadmanager.core.QuietHours.parse(quietHours) == null,
                singleLine = true, modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
            )
            Hint("While downloads run, Android shows one quiet notification with their progress and Pause all; it can't be turned off while the app downloads in the background.")
            TextButton(onClick = {
                runCatching { context.startActivity(Intent(SystemSettings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(SystemSettings.EXTRA_APP_PACKAGE, context.packageName)) }
            }) { Text(tr("Sounds and other notification settings")) }

            Section("Scheduler")
            Toggle("Start downloads kept for later", "Every day at ${"%02d:%02d".format(minutes / 60, minutes % 60)}", schedule) {
                schedule = it; s.scheduleEnabled = it; Scheduler.apply(context)
            }
            if (schedule) {
                TextButton(onClick = { pickTime = true }) { Text(tr("Change time")) }
                if (!Scheduler.canExact(context) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    Hint("Android may delay scheduled downloads or ask you to tap a notification. Allow alarms for exact timing.")
                    TextButton(onClick = {
                        runCatching { context.startActivity(Intent(SystemSettings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:${context.packageName}"))) }
                    }) { Text(tr("Allow alarms")) }
                }
            }

            Section("Browser and privacy")
            Toggle("Block third-party cookies", "Sites can't track you across other sites.", block3p) {
                block3p = it; s.blockThirdPartyCookies = it; browser.applyPrivacy()
            }
            Text(tr("Search engine: ") + com.nexonic.downloadmanager.core.SearchEngines.of(engine).name, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp))
            TextButton(onClick = { pickEngine = true }) { Text(tr("Change search engine")) }
            if (pickEngine) AlertDialog(
                onDismissRequest = { pickEngine = false },
                title = { Text(tr("Search engine")) },
                text = {
                    Column {
                        com.nexonic.downloadmanager.core.SearchEngines.all.forEach { e ->
                            Row(Modifier.fillMaxWidth().clickable { engine = e.id; s.searchEngine = e.id; pickEngine = false }.padding(vertical = 10.dp),
                                verticalAlignment = Alignment.CenterVertically) {
                                androidx.compose.material3.RadioButton(selected = engine == e.id, onClick = { engine = e.id; s.searchEngine = e.id; pickEngine = false })
                                Text(e.name)
                            }
                        }
                    }
                },
                confirmButton = { TextButton(onClick = { pickEngine = false }) { Text(tr("Close")) } },
            )
            OutlinedTextField(home, { home = it; if (com.nexonic.downloadmanager.core.Links.isWebLink(it)) s.homePage = it },
                label = { Text(tr("Home page")) }, singleLine = true, modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp))
            TextButton(onClick = { browser.clearData(); message("Browser data cleared") }) { Text(tr("Clear cookies, cache and history")) }
            TextButton(onClick = { confirmClear = true }) { Text(tr("Clear finished downloads from the list")) }
            Hint("No account, no ads, no tracking. Cookies and passwords for downloads are encrypted on this phone and sent only to their own site.")

            Section("Video engine")
            Hint("Saves videos from YouTube, Facebook, Instagram, TikTok, X and 1,000+ other sites (the same video engine as the desktop apps). It updates itself once a day.")
            Text(tr("Version: ") + (engineVersion ?: "starting…"), style = MaterialTheme.typography.bodyMedium)
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
            }) { Text(tr(if (updating) "Checking…" else "Update video engine")) }

            Section("Crash reports")
            Hint("When the app stops because of an error, a report is saved on this phone. It is never sent anywhere; share it with a bug report if you like.")
            TextButton(onClick = {
                val report = com.nexonic.downloadmanager.core.CrashReports.latest(java.io.File(context.filesDir, "crash-reports"))
                if (report == null) message("No crash reports.")
                else runCatching {
                    context.startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain")
                        .putExtra(Intent.EXTRA_TEXT, report.readText()), tr("Share crash report")))
                }
            }) { Text(tr("Share the latest crash report")) }

            Section("About")
            Text("Nexonic Download Manager ${BuildConfig.VERSION_NAME}", style = MaterialTheme.typography.bodyLarge)
            Hint("Free and open source by Shahrukh Khalid. Its own code is MIT or Apache-2.0; the app as a whole is GPL-3.0 because of the video engine.")
            TextButton(onClick = { openLink(context, "https://shahrukhalid.github.io/nexonic-download-manager/") }) { Text(tr("Website")) }
            TextButton(onClick = { openLink(context, "https://github.com/Shahrukhalid/nexonic-download-manager") }) { Text(tr("Source code and help")) }
            TextButton(onClick = { openLink(context, "https://github.com/Shahrukhalid/nexonic-download-manager/issues/new") }) { Text(tr("Report a problem")) }
            TextButton(onClick = { openLink(context, "https://www.patreon.com/Shahrukhalid") }) { Text(tr("Support the Project")) }
            TextButton(onClick = { showLicenses = true }) { Text(tr("Open-source licenses")) }
            Hint("Only download what you have the right to save. Copy-protected services are not supported.")
            Spacer(Modifier.height(32.dp))
        }
    }

    if (showLicenses) {
        val text = remember { licenseTexts(context) }
        AlertDialog(
            onDismissRequest = { showLicenses = false },
            title = { Text(tr("Open-source licenses")) },
            text = { Text(text, style = MaterialTheme.typography.bodySmall, modifier = Modifier.verticalScroll(rememberScrollState())) },
            confirmButton = { TextButton(onClick = { showLicenses = false }) { Text(tr("Close")) } },
        )
    }

    if (pickTime) {
        val state = rememberTimePickerState(initialHour = minutes / 60, initialMinute = minutes % 60)
        AlertDialog(
            onDismissRequest = { pickTime = false },
            title = { Text(tr("Start at")) },
            text = { TimePicker(state) },
            confirmButton = {
                TextButton(onClick = {
                    minutes = state.hour * 60 + state.minute; s.scheduleMinutes = minutes; Scheduler.apply(context); pickTime = false
                }) { Text(tr("Save")) }
            },
            dismissButton = { TextButton(onClick = { pickTime = false }) { Text(tr("Cancel")) } },
        )
    }
    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text(tr("Clear finished downloads?")) },
            text = { Text(tr("They are removed from the list. The files stay in your Downloads folder.")) },
            confirmButton = { TextButton(onClick = { Downloads.clearFinished(); confirmClear = false; message("List cleared") }) { Text(tr("Clear")) } },
            dismissButton = { TextButton(onClick = { confirmClear = false }) { Text(tr("Cancel")) } },
        )
    }
}

private fun openLink(context: android.content.Context, url: String) {
    runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
}

@Composable
private fun Section(title: String) {
    Spacer(Modifier.height(20.dp))
    Text(tr(title), style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.SemiBold)
    HorizontalDivider(Modifier.padding(top = 6.dp, bottom = 10.dp))
}

/** A titled row of choices (radio buttons) that scrolls sideways when it does not fit. */
@Composable
private fun Choices(title: String, options: List<Pair<String, String>>, selected: String, onPick: (String) -> Unit) {
    Text(tr(title), style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp))
    Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
        options.forEach { (code, label) ->
            androidx.compose.material3.RadioButton(selected = selected == code, onClick = { onPick(code) })
            Text(tr(label), Modifier.clickable { onPick(code) }.padding(end = 12.dp))
        }
    }
}

@Composable
internal fun Hint(text: String) {
    Text(tr(text), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
private fun Toggle(title: String, subtitle: String, value: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { onChange(!value) }.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(tr(title), style = MaterialTheme.typography.bodyLarge)
            Hint(subtitle)
        }
        Switch(value, onChange)
    }
}

/** The notices and license texts shipped in assets/licenses, NOTICES.txt first. */
private fun licenseTexts(context: android.content.Context): String {
    val names = runCatching { context.assets.list("licenses")?.toList() }.getOrNull().orEmpty()
        .sortedWith(compareBy({ it != "NOTICES.txt" }, { it }))
    return names.joinToString("\n\n" + "=".repeat(40) + "\n\n") { name ->
        runCatching { context.assets.open("licenses/$name").bufferedReader().use { it.readText() } }.getOrDefault("")
    }
}
