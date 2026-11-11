@file:OptIn(ExperimentalMaterial3Api::class)

package com.nexonic.downloadmanager.ui

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.util.Size
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Movie
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nexonic.downloadmanager.core.FileCategory
import com.nexonic.downloadmanager.core.Playlists
import com.nexonic.downloadmanager.core.tr
import com.nexonic.downloadmanager.data.DownloadItem
import com.nexonic.downloadmanager.data.Downloads
import com.nexonic.downloadmanager.video.VideoEngine
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.DateFormat
import java.util.Date

/** Lists a playlist's or channel's videos; the chosen ones open in the Add window ([onAdd] gets one link per line). */
@Composable
private fun ChoiceRow(title: String, options: List<Pair<String, String>>, selected: String, pick: (String) -> Unit) {
    Text(tr(title), style = MaterialTheme.typography.bodyMedium)
    Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
        options.forEach { (k, label) -> FilterChip(selected = selected == k, onClick = { pick(k) }, label = { Text(tr(label)) }, modifier = Modifier.padding(end = 6.dp)) }
    }
}

/**
 * Profile Batch: one link to a profile, channel, playlist or page → every video open to everyone there (no login, no cookies),
 * following the site's pages, filtered, without repeats, downloaded in the chosen quality.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProfileBatchSheet(link: String, onDismiss: () -> Unit, message: (String) -> Unit) {
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val scope = rememberCoroutineScope()
    val pb = com.nexonic.downloadmanager.core.ProfileBatch
    var input by remember { mutableStateOf(link) }
    var quality by remember { mutableStateOf("best") }
    var mode by remember { mutableStateOf("all") }
    var autoStart by remember { mutableStateOf(true) }
    var keywords by remember { mutableStateOf("") }
    var minMinutes by remember { mutableStateOf("") }
    var maxMinutes by remember { mutableStateOf("") }
    var fromDay by remember { mutableStateOf("") }
    var toDay by remember { mutableStateOf("") }
    var kind by remember { mutableStateOf("all") }
    var maxCount by remember { mutableStateOf(pb.DEFAULT_MAX.toString()) }
    var every by remember { mutableStateOf("0") }
    var showFilters by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("") }
    var found by remember { mutableStateOf(listOf<com.nexonic.downloadmanager.core.BatchEntry>()) }
    var owner by remember { mutableStateOf("") }
    var preview by remember { mutableStateOf(listOf<com.nexonic.downloadmanager.core.BatchEntry>()) }
    var chosen by remember { mutableStateOf(setOf<String>()) }
    val stopId = remember { "scan-" + System.nanoTime() }
    var exportFormat by remember { mutableStateOf("txt") }
    val context = LocalContext.current
    val saveFile = androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.CreateDocument("*/*")) { uri ->
        uri ?: return@rememberLauncherForActivityResult
        runCatching { context.contentResolver.openOutputStream(uri)?.use { it.write(pb.export(found, exportFormat).toByteArray()) } }
            .onSuccess { message(tr("Saved")) }.onFailure { message(it.message ?: tr("Could not save")) }
    }
    val dayFormat = remember { java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.US).apply { timeZone = java.util.TimeZone.getTimeZone("UTC"); isLenient = false } }
    fun filter() = com.nexonic.downloadmanager.core.BatchFilter(
        from = runCatching { dayFormat.parse(fromDay.trim())?.time }.getOrNull(), to = runCatching { dayFormat.parse(toDay.trim())?.time }.getOrNull(),
        keywords = keywords, minSeconds = minMinutes.toIntOrNull()?.times(60), maxSeconds = maxMinutes.toIntOrNull()?.times(60), kind = kind,
        max = (maxCount.toIntOrNull() ?: pb.DEFAULT_MAX).coerceIn(1, pb.LIMIT))

    fun saved(url: String) = Downloads.profiles.firstOrNull { pb.key(it.url) == pb.key(url) }

    fun download(url: String, take: List<com.nexonic.downloadmanager.core.BatchEntry>, counts: com.nexonic.downloadmanager.core.BatchCounts) {
        val added = Downloads.queueBatch(take, quality, autoStart)
        status = (if (owner.isEmpty()) "" else "$owner: ") + counts.copy(queued = added).text
        val hours = every.toInt()
        val old = saved(url)
        if (old != null || hours > 0 || mode == "new") {
            val p = (old ?: com.nexonic.downloadmanager.core.SavedProfile(java.util.UUID.randomUUID().toString(), url, owner, quality, filter(), hours))
                .copy(title = owner, quality = quality, filter = filter(), checkHours = hours, lastChecked = System.currentTimeMillis(), lastResult = status,
                    seen = (old?.seen ?: emptySet()) + found.map { pb.key(it.url) })
            Downloads.profiles = Downloads.profiles.filter { it.id != p.id } + p
        }
        preview = emptyList()
    }

    fun find() {
        val url = input.trim()
        if (!url.startsWith("http://", true) && !url.startsWith("https://", true)) { status = tr("Paste a web link (http or https)."); return }
        busy = true; preview = emptyList()
        status = tr("Looking for videos… (large profiles can take a few minutes)")
        val f = filter()
        scope.launch {
            val r = withContext(Dispatchers.IO) {
                runCatching { Downloads.discover(url, f.max, false, stopId) { n -> if (n > 0) scope.launch { status = tr("%d found so far…").format(n) } } }
            }
            busy = false
            r.onSuccess { (entries, name) ->
                found = entries; owner = name
                val (take, counts) = pb.choose(entries, f, Downloads::alreadyDownloaded, if (mode == "new") saved(url)?.seen else null)
                when {
                    mode == "preview" -> { preview = take; chosen = take.map { it.url }.toSet(); status = (if (name.isEmpty()) "" else "$name: ") + counts.copy(queued = 0).text }
                    take.isEmpty() -> status = (if (name.isEmpty()) "" else "$name: ") + counts.copy(queued = 0).text
                    else -> download(url, take, counts)
                }
            }.onFailure {
                status = if (it is com.yausername.youtubedl_android.YoutubeDL.CanceledException) tr("Stopped. The videos found so far are kept; Find videos continues from there.")
                else it.message ?: tr("The list of videos could not be read.")
            }
        }
    }
    LaunchedEffect(Unit) { if (input.isNotBlank()) find() }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp).navigationBarsPadding().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(tr("Profile Batch"), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
            Text(tr("Paste one link to a profile, channel, playlist or page. Nexonic finds every video that is open to everyone (no login needed), skips the ones you already have and downloads the rest."),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            OutlinedTextField(input, { input = it }, label = { Text(tr("Link")) }, singleLine = true, modifier = Modifier.fillMaxWidth())
            ChoiceRow("Quality", pb.qualities.map { it.key to it.title }, quality) { quality = it }
            ChoiceRow("Download", listOf("all" to "All I don't have", "new" to "Only new", "preview" to "Preview and choose"), mode) { mode = it }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(autoStart, { autoStart = it }); Text(tr("Start downloading straight away"), Modifier.clickable { autoStart = !autoStart })
            }
            ChoiceRow("Look for new videos", listOf("0" to "Never", "1" to "Every hour", "6" to "Every 6 hours", "24" to "Every day", "168" to "Every week"), every) { every = it }
            TextButton(onClick = { showFilters = !showFilters }) { Text(tr(if (showFilters) "Hide filters" else "Filters")) }
            if (showFilters) {
                OutlinedTextField(keywords, { keywords = it }, label = { Text(tr("Title contains (words; !word leaves it out)")) }, singleLine = true, modifier = Modifier.fillMaxWidth())
                Row {
                    OutlinedTextField(fromDay, { fromDay = it }, label = { Text(tr("From (YYYY-MM-DD)")) }, singleLine = true, modifier = Modifier.weight(1f).padding(end = 6.dp))
                    OutlinedTextField(toDay, { toDay = it }, label = { Text(tr("To (YYYY-MM-DD)")) }, singleLine = true, modifier = Modifier.weight(1f))
                }
                Row {
                    OutlinedTextField(minMinutes, { minMinutes = it }, label = { Text(tr("Shortest (min)")) }, singleLine = true, modifier = Modifier.weight(1f).padding(end = 6.dp))
                    OutlinedTextField(maxMinutes, { maxMinutes = it }, label = { Text(tr("Longest (min)")) }, singleLine = true, modifier = Modifier.weight(1f).padding(end = 6.dp))
                    OutlinedTextField(maxCount, { maxCount = it }, label = { Text(tr("At most")) }, singleLine = true, modifier = Modifier.weight(1f))
                }
                ChoiceRow("Type", listOf("all" to "Everything", "videos" to "Videos", "shorts" to "Shorts and reels", "live" to "Live streams"), kind) { kind = it }
                Text(tr("A site that doesn't say when a video was published or how long it is: those videos are kept."), style = MaterialTheme.typography.bodySmall)
            }
            if (busy) LinearProgressIndicator(Modifier.fillMaxWidth())
            if (status.isNotEmpty()) Text(status, style = MaterialTheme.typography.bodySmall)
            if (preview.isNotEmpty()) LazyColumn(Modifier.fillMaxWidth().heightIn(max = 320.dp)) {
                items(preview, key = { it.url }) { e ->
                    Row(Modifier.fillMaxWidth().clickable { chosen = if (e.url in chosen) chosen - e.url else chosen + e.url }, verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(e.url in chosen, { on -> chosen = if (on) chosen + e.url else chosen - e.url })
                        Text(e.title, Modifier.weight(1f), maxLines = 2, overflow = TextOverflow.Ellipsis)
                        e.duration?.takeIf { it > 0 }?.let { Text(Playlists.duration(it), style = MaterialTheme.typography.bodySmall) }
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(bottom = 16.dp), horizontalArrangement = Arrangement.End, verticalAlignment = Alignment.CenterVertically) {
                if (found.isNotEmpty()) listOf("txt", "csv", "json").forEach { f ->
                    TextButton(onClick = { exportFormat = f; saveFile.launch((owner.ifEmpty { "videos" }) + "." + f) }) { Text(f.uppercase()) }
                }
                if (busy) TextButton(onClick = { VideoEngine.cancel(stopId) }) { Text(tr("Stop")) }
                if (preview.isNotEmpty()) Button(onClick = {
                    val take = preview.filter { it.url in chosen }
                    download(input.trim(), take, com.nexonic.downloadmanager.core.BatchCounts(found.size, take.size, 0, found.size - take.size, 0))
                }, enabled = chosen.isNotEmpty()) { Text(tr("Download selected")) }
                else Button(onClick = { find() }, enabled = !busy) { Text(tr("Find videos")) }
            }
        }
    }
}

/** Finished videos and music with preview pictures; tap to play. */
@Composable
fun MediaLibrarySheet(all: List<DownloadItem>, onDismiss: () -> Unit, message: (String) -> Unit) {
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val context = LocalContext.current
    var kind by remember { mutableStateOf<FileCategory?>(null) }
    val media = all
        .filter { it.isFinished && it.contentUri != null && (it.category == FileCategory.VIDEO || it.category == FileCategory.MUSIC) }
        .filter { kind == null || it.category == kind }
        .sortedByDescending { it.finishedAt ?: it.addedAt }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp)) {
            Text(tr("Media library"), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
            Row(Modifier.padding(vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                FilterChip(kind == null, { kind = null }, label = { Text(tr("All")) })
                FilterChip(kind == FileCategory.VIDEO, { kind = FileCategory.VIDEO }, label = { Text(tr("Videos")) })
                FilterChip(kind == FileCategory.MUSIC, { kind = FileCategory.MUSIC }, label = { Text(tr("Music")) })
                Text(if (media.size == 1) "1 file" else "${media.size} files", style = MaterialTheme.typography.bodySmall)
            }
            if (media.isEmpty()) {
                Text(tr("Finished videos and music appear here."), Modifier.padding(vertical = 32.dp), color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                LazyVerticalGrid(
                    GridCells.Adaptive(160.dp), Modifier.fillMaxWidth().heightIn(max = 620.dp),
                    contentPadding = PaddingValues(bottom = 24.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    items(media, key = { it.id }) { item -> MediaCard(item) { play(context, item, message) } }
                }
            }
        }
    }
}

@Composable
private fun MediaCard(item: DownloadItem, onClick: () -> Unit) {
    val context = LocalContext.current
    val isVideo = item.category == FileCategory.VIDEO
    var picture by remember(item.contentUri) { mutableStateOf(Thumbnails.cached(item.contentUri)) }
    LaunchedEffect(item.contentUri) {
        if (picture == null && isVideo) picture = withContext(Dispatchers.IO) { Thumbnails.load(context, item.contentUri) }
    }
    Column(Modifier.clickable(onClick = onClick)) {
        Box(
            Modifier.fillMaxWidth().aspectRatio(16f / 9f).clip(RoundedCornerShape(8.dp)).background(MaterialTheme.colorScheme.surfaceVariant),
            contentAlignment = Alignment.Center,
        ) {
            val p = picture
            if (p != null) Image(p.asImageBitmap(), null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
            else Icon(if (isVideo) Icons.Outlined.Movie else Icons.Outlined.MusicNote, null, Modifier.size(36.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Text(item.fileName, Modifier.padding(top = 4.dp), maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.SemiBold,
            style = MaterialTheme.typography.bodyMedium)
        Text(
            listOfNotNull(item.size?.let { android.text.format.Formatter.formatShortFileSize(context, it) },
                DateFormat.getDateInstance(DateFormat.MEDIUM).format(Date(item.finishedAt ?: item.addedAt))).joinToString("   "),
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

/** Preview pictures from the system (or a frame at four seconds), kept in memory while the app runs. */
private object Thumbnails {
    private val cache = android.util.LruCache<String, Bitmap>(80)

    fun cached(uri: String?): Bitmap? = uri?.let { cache.get(it) }

    fun load(context: Context, uri: String?): Bitmap? {
        if (uri == null) return null
        val u = Uri.parse(uri)
        val bmp = runCatching { context.contentResolver.loadThumbnail(u, Size(480, 270), null) }.getOrNull()
            ?: runCatching {
                MediaMetadataRetriever().run {
                    try {
                        setDataSource(context, u)
                        getFrameAtTime(4_000_000, MediaMetadataRetriever.OPTION_CLOSEST_SYNC) ?: getFrameAtTime(0)
                    } finally { release() }
                }
            }.getOrNull()?.let { scale(it) }
        bmp?.let { cache.put(uri, it) }
        return bmp
    }

    private fun scale(b: Bitmap): Bitmap {
        if (b.width <= 480) return b
        val h = (b.height * 480f / b.width).toInt().coerceAtLeast(1)
        return Bitmap.createScaledBitmap(b, 480, h, true).also { if (it !== b) b.recycle() }
    }
}

private fun play(context: Context, item: DownloadItem, message: (String) -> Unit) {
    val uri = item.contentUri?.let { Uri.parse(it) } ?: return message("The file is not available")
    val type = context.contentResolver.getType(uri) ?: if (item.category == FileCategory.MUSIC) "audio/*" else "video/*"
    val intent = Intent(Intent.ACTION_VIEW).setDataAndType(uri, type).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    runCatching { context.startActivity(Intent.createChooser(intent, tr("Open with"))) }.onFailure { message("No app can open this file") }
}
