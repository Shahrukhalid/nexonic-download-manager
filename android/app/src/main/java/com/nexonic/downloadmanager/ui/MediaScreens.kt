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
import com.nexonic.downloadmanager.video.VideoEngine
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.DateFormat
import java.util.Date

/** Lists a playlist's or channel's videos; the chosen ones open in the Add window ([onAdd] gets one link per line). */
@Composable
fun PlaylistSheet(link: String, onAdd: (String) -> Unit, onDismiss: () -> Unit) {
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val scope = rememberCoroutineScope()
    var input by remember { mutableStateOf(link) }
    var reading by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("") }
    var entries by remember { mutableStateOf(listOf<Playlists.Entry>()) }
    var chosen by remember { mutableStateOf(setOf<String>()) }

    fun read() {
        val url = input.trim()
        if (!url.startsWith("http://", true) && !url.startsWith("https://", true)) { status = tr("Paste a web link (http or https)."); return }
        reading = true
        status = tr("Reading the list of videos… (large channels can take a minute)")
        scope.launch {
            val r = withContext(Dispatchers.IO) { runCatching { VideoEngine.readPlaylist(url) } }
            reading = false
            r.onSuccess { p ->
                entries = p.entries
                chosen = p.entries.map { it.url }.toSet()
                val count = if (p.entries.size == 1) "1 video" else "${p.entries.size} videos"
                status = (if (p.title.isEmpty()) "" else p.title + ": ") + count +
                    if (p.entries.size >= Playlists.MAX_ENTRIES) " (the first ${Playlists.MAX_ENTRIES})" else ""
            }.onFailure { status = it.message ?: tr("The list of videos could not be read.") }
        }
    }
    LaunchedEffect(Unit) { if (input.isNotBlank()) read() }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp).navigationBarsPadding(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(tr("Add playlist or channel"), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
            Text(tr("Paste a playlist or channel link (YouTube, Vimeo, SoundCloud, Bandcamp and other video sites), then choose its videos."),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            OutlinedTextField(input, { input = it }, label = { Text(tr("Playlist or channel link")) }, singleLine = true, modifier = Modifier.fillMaxWidth())
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = { read() }, enabled = !reading) { Text(tr("Read videos")) }
                if (entries.isNotEmpty()) TextButton(onClick = {
                    chosen = if (chosen.size == entries.size) emptySet() else entries.map { it.url }.toSet()
                }) { Text(tr(if (chosen.size == entries.size) "Deselect all" else "Select all")) }
            }
            if (reading) LinearProgressIndicator(Modifier.fillMaxWidth())
            if (status.isNotEmpty()) Text(status, style = MaterialTheme.typography.bodySmall)
            if (entries.isNotEmpty()) LazyColumn(Modifier.fillMaxWidth().heightIn(max = 380.dp)) {
                items(entries, key = { it.url }) { e ->
                    Row(Modifier.fillMaxWidth().clickable { chosen = if (e.url in chosen) chosen - e.url else chosen + e.url },
                        verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(e.url in chosen, { on -> chosen = if (on) chosen + e.url else chosen - e.url })
                        Text(e.title, Modifier.weight(1f), maxLines = 2, overflow = TextOverflow.Ellipsis)
                        e.duration?.takeIf { it > 0 }?.let {
                            Text(Playlists.duration(it), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(bottom = 16.dp), horizontalArrangement = Arrangement.End) {
                TextButton(onClick = onDismiss) { Text(tr("Cancel")) }
                Button(onClick = { onAdd(entries.filter { it.url in chosen }.joinToString("\n") { it.url }) }, enabled = chosen.isNotEmpty()) {
                    Text(if (chosen.size == 1) tr("Add 1 video") else "${tr("Add videos")} (${chosen.size})")
                }
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
