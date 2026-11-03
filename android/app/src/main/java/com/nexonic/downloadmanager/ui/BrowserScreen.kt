@file:OptIn(ExperimentalMaterial3Api::class)

package com.nexonic.downloadmanager.ui

import android.view.ViewGroup
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.ArrowForward
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Download
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.nexonic.downloadmanager.UiState
import com.nexonic.downloadmanager.browser.Browser
import com.nexonic.downloadmanager.browser.Found
import com.nexonic.downloadmanager.core.FileCategory
import com.nexonic.downloadmanager.data.Downloads

@Composable
fun BrowserScreen(browser: Browser, ui: UiState) {
    val url by browser.url.collectAsState()
    val progress by browser.progress.collectAsState()
    val canBack by browser.canBack.collectAsState()
    val canForward by browser.canForward.collectAsState()
    val found by browser.found.collectAsState()
    var address by remember { mutableStateOf(url) }
    var editing by remember { mutableStateOf(false) }
    val focus = LocalFocusManager.current

    LaunchedEffect(url) { if (!editing) address = url }

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().statusBarsPadding().padding(horizontal = 4.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { browser.back() }, enabled = canBack) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Back") }
            IconButton(onClick = { browser.forward() }, enabled = canForward) { Icon(Icons.AutoMirrored.Outlined.ArrowForward, "Forward") }
            OutlinedTextField(
                value = if (editing) address else displayAddress(url),
                onValueChange = { address = it },
                singleLine = true,
                leadingIcon = if (!editing && url.startsWith("https://")) ({ Icon(Icons.Outlined.Lock, "Secure", Modifier.size(16.dp)) }) else null,
                placeholder = { Text("Search or type an address") },
                textStyle = MaterialTheme.typography.bodyMedium,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
                keyboardActions = KeyboardActions(onGo = { browser.go(address); editing = false; focus.clearFocus() }),
                shape = RoundedCornerShape(24.dp),
                modifier = Modifier.weight(1f).heightIn(min = 48.dp).onFocusChanged { f ->
                    if (f.isFocused && !editing) { editing = true; address = url } else if (!f.isFocused) editing = false
                },
            )
            if (progress in 1..99) IconButton(onClick = { browser.stop() }) { Icon(Icons.Outlined.Close, "Stop") }
            else IconButton(onClick = { browser.reload() }) { Icon(Icons.Outlined.Refresh, "Reload") }
            IconButton(onClick = { browser.go(Downloads.settings.homePage) }) { Icon(Icons.Outlined.Home, "Home") }
        }
        if (progress in 1..99) LinearProgressIndicator(progress = { progress / 100f }, modifier = Modifier.fillMaxWidth().height(2.dp))
        else Spacer(Modifier.height(2.dp))

        Box(Modifier.fillMaxSize()) {
            AndroidView(
                factory = { (browser.view.parent as? ViewGroup)?.removeView(browser.view); browser.view },
                modifier = Modifier.fillMaxSize(),
            )
            if (com.nexonic.downloadmanager.core.VideoPages.isVideoPage(url)) {
                // Video sites (YouTube, Facebook, Instagram…): the video engine saves the video playing on this page.
                ExtendedFloatingActionButton(
                    onClick = { ui.addText = url },
                    icon = { Icon(Icons.Outlined.Download, null) },
                    text = { Text("Download this video") },
                    modifier = Modifier.align(Alignment.BottomEnd).padding(16.dp),
                )
            } else if (found.isNotEmpty()) {
                ExtendedFloatingActionButton(
                    onClick = { ui.showFound = true },
                    icon = { Icon(Icons.Outlined.Download, null) },
                    text = { Text("${found.size} found") },
                    modifier = Modifier.align(Alignment.BottomEnd).padding(16.dp),
                )
            }
        }
    }

    if (ui.showFound) {
        val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
        ModalBottomSheet(onDismissRequest = { ui.showFound = false }, sheetState = sheet) {
            Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).navigationBarsPadding().padding(bottom = 24.dp)) {
            Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp)) {
                Text("Found on this page", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
                Text(
                    "Videos, music and files on ${browser.host()}. Streaming and copy-protected video can't be saved.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(Modifier.height(8.dp))
            }
            found.sortedBy { order(it) }.forEach { f ->
                FoundRow(f) {
                    ui.showFound = false
                    ui.addOffer = browser.offerFor(f.url, f.label)
                }
            }
            }
        }
    }
}

private fun order(f: Found) = when (f.kind) { "Video" -> 0; "Audio" -> 1; else -> 2 }

private fun displayAddress(url: String): String =
    url.removePrefix("https://").removePrefix("http://").removePrefix("www.").trimEnd('/')

@Composable
private fun FoundRow(f: Found, onDownload: () -> Unit) {
    val category = FileCategory.of(f.name)
    val color = categoryColor(category)
    Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(38.dp).clip(RoundedCornerShape(10.dp)).background(color.copy(alpha = 0.14f)), contentAlignment = Alignment.Center) {
            Icon(categoryIcon(category), null, tint = color, modifier = Modifier.size(20.dp))
        }
        Spacer(Modifier.width(12.dp))
        Column(Modifier.weight(1f)) {
            Text(f.name, maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Medium)
            Text(listOfNotNull(f.kind, f.label).joinToString(" · "), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
        }
        FilledTonalButton(onClick = onDownload) { Text("Download") }
    }
}
