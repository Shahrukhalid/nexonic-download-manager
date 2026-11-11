package com.nexonic.downloadmanager.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.nexonic.downloadmanager.core.FeedSubscription
import com.nexonic.downloadmanager.core.Feeds
import com.nexonic.downloadmanager.core.Links
import com.nexonic.downloadmanager.core.tr
import com.nexonic.downloadmanager.data.Downloads
import java.text.DateFormat
import java.util.Date

/** Downloads → RSS feeds: feeds whose new items are downloaded when their title matches the rules. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FeedsSheet(onDismiss: () -> Unit) {
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var version by remember { mutableIntStateOf(0) }   // redraws after a change or a check
    var editing by remember { mutableStateOf<FeedSubscription?>(null) }
    var checking by remember { mutableStateOf(false) }
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        Column(Modifier.padding(horizontal = 20.dp).padding(bottom = 24.dp)) {
            Text(tr("RSS Feeds"), style = MaterialTheme.typography.titleLarge)
            Text(tr("New items of these feeds are downloaded when their title matches the rule: files and videos, and torrents when torrents are on. A rule lists words that must all be in the title, or a /regular expression/."),
                style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(vertical = 8.dp))
            val list = remember(version) { synchronized(Downloads) { Downloads.feeds.toList() } }
            LazyColumn(Modifier.heightIn(max = 360.dp)) {
                items(list, key = { it.id }) { f ->
                    Column(Modifier.fillMaxWidth().clickable { editing = f.copy(seen = f.seen.toMutableList(), episodes = f.episodes.toMutableList()) }.padding(vertical = 8.dp)) {
                        Text((if (f.enabled) "" else "(off) ") + f.name.ifEmpty { f.url }, style = MaterialTheme.typography.bodyLarge)
                        Text(f.include.ifEmpty { tr("every item") }, style = MaterialTheme.typography.bodySmall)
                        Text(if (f.lastChecked == 0L) tr("not checked yet")
                            else DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT).format(Date(f.lastChecked)) + ": " + f.lastResult,
                            style = MaterialTheme.typography.bodySmall)
                    }
                    HorizontalDivider()
                }
            }
            Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.End) {
                TextButton(onClick = { editing = FeedSubscription() }) { Text(tr("Add Feed…")) }
                TextButton(enabled = list.isNotEmpty() && !checking, onClick = {
                    checking = true
                    Downloads.checkFeeds(all = true) { checking = false; version++ }
                }) { Text(tr(if (checking) "Checking…" else "Check Now")) }
                TextButton(onClick = onDismiss) { Text(tr("Close")) }
            }
        }
    }
    editing?.let { feed ->
        FeedEditor(feed, onDone = { saved, remove ->
            synchronized(Downloads) {
                val i = Downloads.feeds.indexOfFirst { it.id == feed.id }
                when {
                    remove -> if (i >= 0) Downloads.feeds.removeAt(i)
                    saved != null && i >= 0 -> Downloads.feeds[i] = saved
                    saved != null -> Downloads.feeds.add(saved)
                }
            }
            if (saved != null || remove) Downloads.saveFeeds()
            editing = null
            version++
        })
    }
}

@Composable
private fun FeedEditor(feed: FeedSubscription, onDone: (FeedSubscription?, Boolean) -> Unit) {
    val isNew = feed.url.isEmpty()
    var url by remember { mutableStateOf(feed.url) }
    var name by remember { mutableStateOf(feed.name) }
    var include by remember { mutableStateOf(feed.include) }
    var exclude by remember { mutableStateOf(feed.exclude) }
    var quality by remember { mutableStateOf(feed.quality) }
    var minSize by remember { mutableStateOf(if (feed.minSizeMB > 0) feed.minSizeMB.toString() else "") }
    var maxSize by remember { mutableStateOf(if (feed.maxSizeMB > 0) feed.maxSizeMB.toString() else "") }
    var interval by remember { mutableStateOf(feed.intervalMinutes.toString()) }
    var oneEach by remember { mutableStateOf(feed.oneOfEachEpisode) }
    var skip by remember { mutableStateOf(feed.skipExisting) }
    var on by remember { mutableStateOf(feed.enabled) }
    var problem by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = { onDone(null, false) },
        title = { Text(tr(if (isNew) "Add Feed" else "Edit Feed")) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                Field(url, "Feed address (RSS or Atom)", false) { url = it }
                Field(name, "Name (optional)", false) { name = it }
                Field(include, "Download items whose title has all of these words (empty = every item)", false) { include = it }
                Field(exclude, "Skip items whose title has any of these words", false) { exclude = it }
                Field(quality, "Preferred qualities, best first (e.g. 1080p 720p)", false) { quality = it }
                Field(minSize, "Smallest file in MB (empty = any)", true) { minSize = it.filter(Char::isDigit) }
                Field(maxSize, "Largest file in MB (empty = any)", true) { maxSize = it.filter(Char::isDigit) }
                Field(interval, "Check every (minutes, 15 or more)", true) { interval = it.filter(Char::isDigit) }
                Tick("Only one download per episode", oneEach) { oneEach = it }
                Tick("Only download items that appear after the feed is added", skip) { skip = it }
                Tick("Check this feed", on) { on = it }
                if (problem.isNotEmpty()) Text(tr(problem), color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val u = url.trim()
                problem = when {
                    !Links.isWebLink(u) -> "The feed address must start with http:// or https://"
                    else -> Feeds.ruleProblem(include) ?: Feeds.ruleProblem(exclude) ?: ""
                }
                if (problem.isNotEmpty()) return@TextButton
                val changed = u != feed.url
                onDone(feed.copy(
                    url = u, name = name.trim(), include = include.trim(), exclude = exclude.trim(), quality = quality.trim(),
                    minSizeMB = minSize.toIntOrNull() ?: 0, maxSizeMB = maxSize.toIntOrNull() ?: 0,
                    intervalMinutes = (interval.toIntOrNull() ?: 30).coerceIn(15, 1440), oneOfEachEpisode = oneEach,
                    skipExisting = skip, enabled = on,
                    lastChecked = if (changed) 0 else feed.lastChecked,
                    seen = if (changed) mutableListOf() else feed.seen, episodes = if (changed) mutableListOf() else feed.episodes,
                ), false)
            }) { Text(tr("Save")) }
        },
        dismissButton = {
            Row {
                if (!isNew) TextButton(onClick = { onDone(null, true) }) { Text(tr("Remove")) }
                TextButton(onClick = { onDone(null, false) }) { Text(tr("Cancel")) }
            }
        },
    )
}

@Composable
private fun Field(value: String, label: String, number: Boolean, set: (String) -> Unit) {
    OutlinedTextField(value, { set(it.take(2000)) }, label = { Text(tr(label)) }, singleLine = true,
        keyboardOptions = if (number) KeyboardOptions(keyboardType = KeyboardType.Number) else KeyboardOptions.Default,
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp))
}

@Composable
private fun Tick(label: String, value: Boolean, set: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { set(!value) }, verticalAlignment = Alignment.CenterVertically) {
        Checkbox(value, set)
        Text(tr(label), style = MaterialTheme.typography.bodyMedium)
    }
}
