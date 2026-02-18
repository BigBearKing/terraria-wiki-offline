/*******************************************************************************
 *                                                                             *
 *                                  COMMON.JS                                  *
 *                                                                             *
 *******************************************************************************/
/**
 * This page contains the main JavaScript for the Terraria Wiki.
 * Certain other JavaScript code is offloaded to gadgets; see [[Special:Gadgets]].
 *
 * Import this JS in a language wiki or the Terraria Mods Wiki via the following line:
mw.loader.load("https://terraria.wiki.gg/load.php?lang=en&modules=site&only=scripts&skin=vector");
 * Put this line as the content of MediaWiki:Common.js.
 * For an example, see the Chinese wiki: https://terraria.wiki.gg/zh/wiki/MediaWiki:Common.js
 */


///////////////////////////////////////////////////////////////////////////////////////////////////////////////
/*!
floating-scroll v3.2.0
https://amphiluke.github.io/floating-scroll/
(c) 2022 Amphiluke
*/
!function(t,i){"object"==typeof exports&&"undefined"!=typeof module?i(require("jquery")):"function"==typeof define&&define.amd?define(["jquery"],i):i((t="undefined"!=typeof globalThis?globalThis:t||self).jQuery)}(this,(function(t){"use strict";var i="horizontal",n="vertical",e={init:function(t,n){var e=this;e.orientationProps=function(t){var n=t===i;return{ORIENTATION:t,SIZE:n?"width":"height",X_SIZE:n?"height":"width",OFFSET_SIZE:n?"offsetWidth":"offsetHeight",OFFSET_X_SIZE:n?"offsetHeight":"offsetWidth",CLIENT_SIZE:n?"clientWidth":"clientHeight",CLIENT_X_SIZE:n?"clientHeight":"clientWidth",INNER_X_SIZE:n?"innerHeight":"innerWidth",SCROLL_SIZE:n?"scrollWidth":"scrollHeight",SCROLL_POS:n?"scrollLeft":"scrollTop",START:n?"left":"top",X_START:n?"top":"left",X_END:n?"bottom":"right"}}(n);var o=t.closest(".fl-scrolls-body");o.length&&(e.scrollBody=o),e.container=t[0],e.visible=!0,e.initWidget(),e.updateAPI(),e.addEventHandlers(),e.skipSyncContainer=e.skipSyncWidget=!1},initWidget:function(){var i=this,n=i.orientationProps,e=n.ORIENTATION,o=n.SIZE,r=n.SCROLL_SIZE,c=i.widget=t('<div class="fl-scrolls" data-orientation="'+e+'"></div>');t("<div></div>").appendTo(c)[o](i.container[r]),c.appendTo(i.container)},addEventHandlers:function(){var i=this;(i.eventHandlers=[{$el:t(window),handlers:{"destroyDetached.fscroll":function(t){"fscroll"===t.namespace&&i.destroyDetachedAPI()}}},{$el:i.scrollBody||t(window),handlers:{scroll:function(){i.updateAPI()},resize:function(){i.updateAPI()}}},{$el:i.widget,handlers:{scroll:function(){i.visible&&!i.skipSyncContainer&&i.syncContainer(),i.skipSyncContainer=!1}}},{$el:t(i.container),handlers:{scroll:function(){i.skipSyncWidget||i.syncWidget(),i.skipSyncWidget=!1},focusin:function(){setTimeout((function(){i.widget&&i.syncWidget()}),0)},"update.fscroll":function(t){"fscroll"===t.namespace&&i.updateAPI()},"destroy.fscroll":function(t){"fscroll"===t.namespace&&i.destroyAPI()}}}]).forEach((function(t){var i=t.$el,n=t.handlers;return i.bind(n)}))},checkVisibility:function(){var t=this,i=t.widget,n=t.container,e=t.scrollBody,o=t.orientationProps,r=o.SCROLL_SIZE,c=o.OFFSET_SIZE,l=o.X_START,s=o.X_END,d=o.INNER_X_SIZE,a=o.CLIENT_X_SIZE,f=i[0][r]<=i[0][c];if(!f){var h=n.getBoundingClientRect(),u=e?e[0].getBoundingClientRect()[s]:window[d]||document.documentElement[a];f=h[s]<=u||h[l]>u}t.visible===f&&(t.visible=!f,i.toggleClass("fl-scrolls-hidden"))},syncContainer:function(){var t=this,i=t.orientationProps.SCROLL_POS,n=t.widget[0][i];t.container[i]!==n&&(t.skipSyncWidget=!0,t.container[i]=n)},syncWidget:function(){var t=this,i=t.orientationProps.SCROLL_POS,n=t.container[i];t.widget[0][i]!==n&&(t.skipSyncContainer=!0,t.widget[0][i]=n)},updateAPI:function(){var i=this,n=i.orientationProps,e=n.SIZE,o=n.X_SIZE,r=n.OFFSET_X_SIZE,c=n.CLIENT_SIZE,l=n.CLIENT_X_SIZE,s=n.SCROLL_SIZE,d=n.START,a=i.widget,f=i.container,h=i.scrollBody,u=f[c],S=f[s];a[e](u),h||a.css(d,f.getBoundingClientRect()[d]+"px"),t("div",a)[e](S),S>u&&a[o](a[0][r]-a[0][l]+1),i.syncWidget(),i.checkVisibility()},destroyAPI:function(){var t=this;t.eventHandlers.forEach((function(t){var i=t.$el,n=t.handlers;return i.unbind(n)})),t.widget.remove(),t.eventHandlers=t.widget=t.container=t.scrollBody=null},destroyDetachedAPI:function(){t.contains(document.body,this.container)||this.destroyAPI()}};t.fn.floatingScroll=function(o,r){if(void 0===o&&(o="init"),void 0===r&&(r={}),"init"===o){var c=r.orientation,l=void 0===c?i:c;if(l!==i&&l!==n)throw new Error("Scrollbar orientation should be either “horizontal” or “vertical”");this.each((function(i,n){return Object.create(e).init(t(n),l)}))}else Object.prototype.hasOwnProperty.call(e,o+"API")&&this.trigger(o+".fscroll");return this},t((function(){t("body [data-fl-scrolls]").each((function(i,n){var e=t(n);e.floatingScroll("init",e.data("flScrolls")||{})}))}))}));
///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * common l10nFactory
 */
var l10nFactory = function($lang, $data) {
	return function ($key) {
		return $data[$key] && ($data[$key][$lang] || $data[$key]['en']) || '';
	};
};

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * common helper
 */
var isEditorActive = function() {
	var urlParams = new URLSearchParams(window.location.search);
	return (
		urlParams.get("action") === "edit" ||
		urlParams.get("action") === "submit" ||
		urlParams.get("veaction") === "edit" ||
		urlParams.get("veaction") === "editsource" ||
		urlParams.get("veaction") === "submit"
	);
};


///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * Handle wide tables
 *
 * Display a horizontal floating scroll bar when the table width exceeds the page width.
 */
$.when($.ready, mw.loader.using(['mediawiki.util'])).then( function() {
	var TABLE_WIDE_CLASS = "table-wide";
	var TABLE_WIDE_INNER_CLASS = "table-wide-inner";

	var handleWideTables = function(tables) {
		var handler = mw.util.debounce(100, function() {
			if(!tables){
				return;
			}
			tables.forEach(function(table) {
				var $table = $(table);
				if(!$table.data('container')){
					$table.data('container', table.parentNode);
				}
				var container = $table.data('container');
				if(!container){
					return;
				}
				var $innerBox = $table.parent();
				var $outerBox = $innerBox.parent();
				var overwide = table.getBoundingClientRect().width > container.getBoundingClientRect().width;
				if($outerBox.hasClass(TABLE_WIDE_CLASS)){
					if(overwide){
						$innerBox.floatingScroll("update");
					}else{
						$outerBox.before($table).remove();
					}
				}else{
					if(overwide) {
						$('<div/>').addClass(TABLE_WIDE_INNER_CLASS).appendTo(
							$('<div/>').addClass(TABLE_WIDE_CLASS).insertBefore($table)
						).append($table).floatingScroll("init").floatingScroll("update");
					}
				}
			});
		});
		handler();
		$(window).on('load', handler).on('resize', handler);
	};

	mw.hook("wikipage.content").add(function() {
		if (!isEditorActive()) {
			var el = document.querySelector("#bodyContent");
			if (el) {
				handleWideTables(el.querySelectorAll("table"));
				$(window).on('resize', function(){
					handleWideTables(el.querySelectorAll("table"));
				});
			}
		}
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * #mw-head collapsing fix
 */
mw.loader.using('skins.vector.legacy.js', function() {
	$.collapsibleTabs.calculateTabDistance = function(){
		return parseInt(window.getComputedStyle(document.getElementById( 'right-navigation' ), '::before').width ) - 1;
	}
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * Make sidebar sections collapsible
 */
$(function(){
	var $root = $(':root');
	var $panel = $('#mw-panel');
	var $portals = $("#mw-panel .vector-menu-portal");
	var s = function(){
		$portals.each(function(index, el){
			var $el = $(el);
			var $id = $el.attr("id");
			if(!$id){
				return;
			}
			// for < 1366px
			$el.removeClass('expanded');
			// for >= 1366px
			if(localStorage.getItem('sidebar_c_'+$id) === "y"){
				$el.addClass('collapsed').find('.vector-menu-content').slideUp(0);
			}
		});
	}
	s();
	$(window).on('resize', s);
	$portals.on("click", "h3", function(event){
		var $el = $(this).parent();
		var $id = $el.attr("id");
		if(!$id){
			return;
		}
		event.stopPropagation();
		var styles = getComputedStyle($root[0]);
		//Note: jQuery's .css() can not handle inexistent custom property properly.
		var sidebarWidth = parseInt(styles.getPropertyValue('--layout-sidebar-width')||styles.getPropertyValue('--main-layout-sidebar-width')) || 250;
		if($panel.width() <= sidebarWidth){
			$el.toggleClass('collapsed');
			if($el.hasClass('collapsed')){ // more consistent between class and slide status.
				localStorage.setItem('sidebar_c_'+$id, "y");
				$el.find('.vector-menu-content').slideUp('fast');
			}
			else{
				localStorage.setItem('sidebar_c_'+$id, "n");
				$el.find('.vector-menu-content').slideDown('fast');
			}
		}
		else{
			$("#mw-panel .vector-menu-portal").not($el).removeClass('expanded');
			$el.toggleClass('expanded');
		}
	});
});

/*** Mobile navigation toggle button ***/
$( document ).ready(function(){
	$('<div class="menu-toggle"/>').insertAfter($('#p-logo')).on("click", function(event){
		event.stopPropagation();
		$(this).toggleClass('expanded');
	});
});


///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * login status mark
 */
$(function(){
	if(mw.config.get("wgUserName") !== null){
		$('body').addClass('logged-in');
	}
	else{
		$('body').addClass('not-logged-in');
	}
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * Content box customization
 */
mw.hook('wikipage.content').add(function() {
	/* Disable triggering of new browser tab when clicking URL links that point to internal wiki addresses (purge, edit, etc) */
	$('a[href^="//'+window.location.hostname+'/"]').removeAttr('target');

	/* Hyperlink required modules in Module namespace */
	// Author: RheingoldRiver
	if (mw.config.get('wgCanonicalNamespace') === 'Module') {
		$('.s1, .s2').each(function () {
			var html = $(this).html();
			// the module name is surrounded by quotes, so we have to remove them
			var quote = html[0];
			var quoteRE = new RegExp('^' + quote + '|' + quote + '$', 'g');
			var name = html.replace(quoteRE, ""); // remove quotes
			// link the module name
			if (name.startsWith("Module:")) {
				var target = encodeURIComponent(name);
				var url = mw.config.get('wgServer') + mw.config.get('wgScript') + '?title=' + target;
				$(this).html(quote + '<a href="' + url + '">' + name + '</a>' + quote);
			}
		});
	}
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * content width toggle
 */
$(function(){
	$body = $('body');
	$('<div id="nav-content-size-toggle"><span></span></div>')
	.prependTo($('#mw-head'))
	.on('click', function(){
		$body.toggleClass('content-size-expanded');
		$(window).trigger('resize');
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////
/**
 * mobile floating fix
 */
$(function(){
	var $contentBox = $('#mw-content-text .mw-parser-output');
	var $elements = $contentBox.children();
	var handle = function(){
		var fullWidth = $contentBox.width();
		if(!fullWidth){
			return;
		}
		var offset = $contentBox.offset().left;
		$elements.removeClass('mobile-floating-fix mobile-fullwidth');

		if(fullWidth > 720){
			return;
		}

		var maxLeft = 0;
		for(var i=$elements.length; i>0; i--){
			var $el = $($elements[i-1]);
			if($el.css('float') == 'right'){
				var left = $el.offset().left;
				if(left - offset < 300 || (maxLeft && left < maxLeft + 12) ){
					$el.addClass('mobile-floating-fix');
					maxLeft = Math.max(maxLeft, left + $el.outerWidth());
					continue;
				}
			}
			maxLeft = 0;
		}
		
		var threshold = Math.min(90, fullWidth*0.25);
		$('#mw-content-text .infobox, #mw-content-text .portable-infobox').each(function(){
			var $el = $(this);
			if(fullWidth - $el.outerWidth() <  threshold){
				$el.addClass('mobile-fullwidth');
			}
		});
	};
	handle();
	$(window).on('resize', mw.util.debounce( handle, 200) );
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * scroll helper for edit
 */
$(function(){
	if( !isEditorActive() || $(window).scrollTop() != 0 ){
		return;
	}
	$(window).scrollTop($('#p-logo').height()-6);
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * AJAX tables
 */
mw.hook('wikipage.content').add(function() {
	var l10n = l10nFactory(mw.config.get('wgUserLanguage'),{
		showData: {
			'en': 'show data',
			'de': 'Daten anzeigen',
			'fr': 'affiche les données',
			'zh': '显示数据',
			'zh-cn': '显示数据'
		},
		wait: {
			'en': 'Please wait, the content is being loaded...',
			'de': 'Bitte warten, der Inhalt wird geladen...',
			'fr': 'Veuillez patienter pendant le chargement du contenu...',
			'pt': 'Por favor espere, o conteúdo está sendo carregado...',
			'ru': 'Пожалуйста, подождите, содержимое загружается...',
			'uk': 'Будь ласка, зачекайте вміст завантажиться…',
			'zh': '请稍候，正在加载内容……',
			'zh-cn': '请稍候，正在加载内容……'
		},
		edit: {
			'en': 'edit',
			'de': 'bearbeiten',
			'fr': 'modifier',
			'pt': 'Editar',
			'ru': 'править',
			'uk': 'редагувати',
			'zh': '编辑',
			'zh-cn': '编辑'
		},
		hide: {
			'en': 'hide',
			'de': 'verbergen',
			'fr': 'masquer',
			'pt': 'Esconder',
			'ru': 'свернуть',
			'uk': 'згорнути',
			'zh': '隐藏',
			'zh-cn': '隐藏'
		},
		show: {
			'en': 'show',
			'de': 'anzeigen',
			'fr': 'afficher',
			'pt': 'Mostrar',
			'ru': 'развернуть',
			'uk': 'розгорнути',
			'zh': '显示',
			'zh-cn': '显示'
		},
		error: {
			'en': 'Unable to load table; the source article for it might not exist.',
			'de': 'Kann Tabelle nicht laden; möglicherweise existiert der Quellartikel nicht.',
			'fr': 'Impossible de charger cette table; l\'article originel ne semble pas exister.',
			'pt': 'Não é possível a carregar tabela; o artigo fonte pode não existir.',
			'ru': 'Не удалось загрузить содержимое; возможно, целевая страница не существует.',
			'uk': 'Неможливо завантажити вміст; можливо, цільова сторінка не існує.',
			'zh': '无法加载表格，其源文章可能不存在。',
			'zh-cn': '无法加载表格，其源文章可能不存在。'
		}
	});
	$("table.ajax").each(function (i) {
		var table = $(this).attr("id", "ajaxTable" + i);
		if (table.data('ajax-already-loaded')) {
			return;
		}
		table.data('ajax-already-loaded', true);
		table.find(".nojs-message").remove();
		var headerLinks = $('<span style="float: right;">').appendTo(table.find('th').first());
		var cell = table.find("td").first();
		var needLink = true;
		cell.parent().show();
		if (cell.hasClass("showLinkHere")) {
			var old = cell.html();
			var rep = old.replace(/\[link\](.*?)\[\/link\]/, '<a href="javascript:;" class="ajax-load-link">$1</a>');
			if (rep !== old) {
				cell.html(rep);
				needLink = false;
			}
		}
		if (needLink){
			headerLinks.html('[<a href="javascript:;" class="ajax-load-link">'+l10n('showData')+'</a>]');
		}
		var removeTerrariaClass = table.data('ajax-remove-terraria-class');
		table.find(".ajax-load-link").parent().addBack().filter('a').click(function(event) {
			event.preventDefault();
			var sourceTitle = table.data('ajax-source-page'), baseLink = mw.config.get('wgScript') + '?';
			cell.text(l10n('wait'));
			$.get(baseLink + $.param({ action: 'render', title: sourceTitle }), function (data) {
				if (!data) {
					return;
				}
				cell.html(data);
				cell.find('.ajaxHide').remove();
				if (removeTerrariaClass) {
					cell.find('.terraria').removeClass('terraria');
				}
				if (cell.find("table.sortable").length) {
					mw.loader.using('jquery.tablesorter', function() {
						cell.find("table.sortable").tablesorter();
					});
				}
				headerLinks.text('[');
				headerLinks.append($('<a>'+l10n('edit')+'</a>').attr('href', baseLink + $.param({ action: 'edit', title: sourceTitle })));
				headerLinks.append(document.createTextNode(']\u00A0['));
				var shown = true;
				$("<a href='javascript:;'>"+l10n('hide')+"</a>").click(function() {
					shown = !shown;
					cell.toggle(shown);
					$(this).text(shown ? l10n('hide') : l10n('show'));
				}).appendTo(headerLinks);
				headerLinks.append(document.createTextNode(']'));
			}).error(function() {
				cell.text(l10n('error'));
			});
		});
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * translation project banner
 */
$(function() {
	var $btn = $('#indic-project #indic-project-flag');
	if (!$btn.length) {
		return;
	}
	var $elementToToggle = $('#indic-project');
	$btn.on('click', function () {
		$elementToToggle.toggleClass(['collapsed', 'expanded']);
	});
});


///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * custom control for [[Template:Sound]]
 * Original ported from https://minecraft.gamepedia.com/MediaWiki:Gadget-sound.js.
 */
mw.hook('wikipage.content').add(function() {
	var l10n = l10nFactory(mw.config.get( 'wgUserLanguage' ),{
		'playTitle': {
			'en': 'Click to play',
			'de': 'Zum Abspielen anklicken',
			'fr': 'Cliquer pour jouer',
			'pt': 'Clique para jogar',
			'pl': 'Naciśnij by odtworzyć',
			'ru': 'Щёлкните, чтобы воспроизвести',
			'zh': '点击播放',
			'zh-cn': '点击播放'
		},
		'stopTitle': {
			'en': 'Click to stop',
			'de': 'Zum Beenden anklicken',
			'fr': 'Cliquer pour arrêter',
			'pt': 'Clique para parar',
			'pl': 'Naciśnij by zatrzymać',
			'ru': 'Щёлкните, чтобы остановить',
			'zh': '点击停止',
			'zh-cn': '点击停止'
		}
	});

	$('.mw-parser-output .sound').prop('title', l10n('playTitle')).on('click', function(e){
		// Ignore links
		if (e.target.tagName === 'A') {
			return;
		}
		var audio = $(this).find('audio')[0];
		if (audio) {
			audio.paused ? audio.play() : audio.pause();
		}
	}).find('audio').on('play', function(){
		// Stop any already playing sounds
		var playing = $('.sound-playing audio')[0];
		playing && playing.pause();
		$(this).closest('.sound').addClass('sound-playing').prop('title', l10n('stopTitle'));
	}).on('pause', function(){
		// Reset back to the start
		this.currentTime = 0;
		$(this).closest('.sound').removeClass('sound-playing').prop('title', l10n('playTitle'));
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * l10n_data_table for [[Template:L10n subtemplate]]
 */
mw.hook('wikipage.content').add(function() {
	$('.l10n-data-table th.lang').on('click', function(){
		var $this = $(this);
		var $lang = $this.data('lang');
		if($lang === 'en'){
			return;
		}
		$this.toggleClass('shrinked')
			.closest('table.l10n-data-table').find('td.'+$lang).toggleClass('shrinked');
		$(window).trigger('resize');
	});
	$('.l10n-data-table th.all-lang').on('click', function(){
		var $this = $(this);
		$this.toggleClass('shrinked');
		if($this.hasClass('shrinked')){
			$this.closest('table.l10n-data-table').find('td.l, th.lang').addClass('shrinked');
			$this.closest('table.l10n-data-table').find('td.en, th.en').removeClass('shrinked');
		}else{
			$this.closest('table.l10n-data-table').find('td.l, th.lang').removeClass('shrinked');
		}
		$(window).trigger('resize');
	});
	//only expand current language
	$('.l10n-data-table').each(function(){
		var $this = $(this);
		var $lang = $this.data('lang');
		if($lang === 'en'){
			return;
		}
		var $th = $this.find('th.lang.'+$lang);
		if ($th.length){
			$this.find('th.all-lang').click();
			$th.click();
		}
	});
});


///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * portlet link for [[Template:Legacy navigation tab]]
 */
mw.hook('wikipage.content').add(function() {
	var elementData = $('#marker-for-new-portlet-link').data();
	if (elementData !== undefined && elementData.linktarget !== undefined) {
		var newId, insertBefore, text, hovertext;
		switch (mw.config.get('wgNamespaceNumber')) {
			case 0:  // namespace is '(Main)'
			case 110:  // namespace is 'Guide'
				newId = 'ca-nstab-' + elementData.i18nNsLegacy;
				insertBefore = '#ca-talk';
				text = elementData.i18nLegacyLabel;
				hovertext = elementData.i18nLegacyTitle;
				break;
			case 11000:  // namespace is 'Legacy'
				newId = 'ca-nstab-main';
				insertBefore = '#ca-nstab-' + elementData.i18nNsLegacy;
				text = elementData.i18nMainLabel;
				hovertext = elementData.i18nMainTitle;
				break;
			default:
				return;
		}
		if (!document.getElementById(newId)) {
			// add the tab, but only if it doesn't exist yet
			// (it might already exist e.g. when using [[mw:Help:Extension:WikiEditor/Realtime Preview|Realtime Preview]])
			mw.util.addPortletLink('p-namespaces', elementData.linktarget, text, newId, hovertext, null, insertBefore);
		}
	}
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * [[Template:Space altitude calculator]]
 */
mw.hook('wikipage.content').add(function() {
	mw.loader.using('oojs-ui').done(function() {
		var inputWidget = new OO.ui.NumberInputWidget({
			classes: ['space-altitude-calculator-input'],
			showButtons: false,
			min: 100,
			max: 9999,
			buttonStep: 100,
		});

		inputWidget.on('change', function () {
			// only perform calculations if the input is valid
			inputWidget.getValidity().then(function () {
				// altitude in feet three tiles below the visible world border
				var inputAltitude = inputWidget.getNumericValue();
				if (inputAltitude == 0) {
					// edge case fix: if the input is blank, getNumericValue() returns 0 and getValidity() returns true
					inputWidget.setValidityFlag(false);
					return;
				}
				// convert to tiles, add 3 to visible world border, add 41 to true world border
				var fullAltitude = inputAltitude / 2 + 3 + 41;
				$('.space-altitude-calculator-output').each(function() {
					var $this = $(this);
					// $this has a data attribute with the altitude percentage as a decimal, e.g. 0.8
					var outputAltitude = parseFloat($this.data('space-altitude-calculator')) * fullAltitude;
					// convert back to feet
					outputAltitude = Math.floor(outputAltitude * 2);
					// display
					$('.value', this).text(outputAltitude);
					$this.show();
				});
			});
		});

		$('.space-altitude-calculator-fakeinput').before(inputWidget.$element);
		$('.space-altitude-calculator-fakeinput').hide();
		$('.space-altitude-calculator-nojs').hide();
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * for other templates
 */
mw.hook('wikipage.content').add(function() {
	/* Select links to new tabs for [[Template:Ilnt]] and [[Template:Elnt]] */
	$('.linkNewTab a').attr('target','_blank');

	/* mode tabs switch for [[Template:Npc infobox]] and [[Template:Npc infobox/tablestart]] and so on */
	$('.modesbox .modetabs .tab').on('click', function(){
		var $this = $(this);
		if($this.hasClass('current')){
			return;
		}
		$this.parent().children().removeClass('current');
		$this.addClass('current');
		$this.closest('.modesbox').removeClass('c-expert c-master c-normal').addClass($this.hasClass('normal')?'c-normal':($this.hasClass('expert')?'c-expert':'c-master'));
	});

	/* [[Template:Spoiler]] */
	$('.spoiler-content').off('click').on('click', function(){
		$(this).toggleClass('show');
	}).find('a').on('click', function(e){
		e.stopPropagation();
	});

	/* [[Template:ToggleBox]] */
	$('.trw-togglehandle').on('click', function(){
		$(this).closest('.trw-toggleable').toggleClass(['toggled', 'not-toggled']);
	});
	var anchor = window.location.hash.substring(1);
	if(anchor){
		var $target = $('#'+$.escapeSelector(decodeURI(anchor).replaceAll(' ', '_')));
		if($target.length){
			$target.first().parents('.trw-toggleable.trw-toggled-with-anchor').toggleClass(['toggled', 'not-toggled']);
		}
	}	
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * Main page layout helper
 */
mw.hook('wikipage.content').add(function() {
	/* Main page responsive breakpoints. */
	// Since the width of the content box may vary, we can not use media query.
	// These values are ported from legacy hydra skin.
	var $btn = $('#box-wikiheader #box-wikiheader-toggle-link');
	if(!$btn.length) {
		return;
	}
	var $content = $('#content');
	var $header = $('#box-wikiheader');

	function initiate_collapsible() {
		var $width = $content.width();
		// $offset is (fullwidth - content width) under hydra skin.
		// Therefore ($width - $offset) is the width of content box.
		var $offset = $width > 980 ? 250 : ($width > 500 ? 42: 12);

		//header
		$header.toggleClass('collapsable', $width < 1300);
		$header.toggleClass('collapsed', $width < 730);

		//row breaks of flexboxes
		$content
			.toggleClass('box-row-l', ($width <= 3500-$offset) && ($width >= 2400-$offset) )
			.toggleClass('box-row-m', ($width <= 2399-$offset) && ($width >= 1670-$offset) )
			.toggleClass('box-row-s', ($width <= 1669-$offset) );

		$('#box-game')
			.toggleClass('width-a', ($width <= 4500-$offset) && ($width >= 3250-$offset) )
			.toggleClass('width-b', ($width <= 3249-$offset) && ($width >= 1670-$offset) )
			.toggleClass('width-c', ($width <= 1669-$offset) )
			.toggleClass('width-d', ($width <= 1200-$offset) )
			.toggleClass('width-e', ($width <= 1160-$offset) )
			.toggleClass('width-f', ($width <=  700-$offset) )
			.toggleClass('width-g', ($width <=  540-$offset) );

		$('#box-news')
			.toggleClass('width-a', ($width >= 1750-$offset) || ($width <= 1669-$offset) )
			.toggleClass('width-b', ($width <=  400-$offset) );

		$('#box-items')
			.toggleClass('width-a', ($width <= 4500-$offset) && ($width >= 3250-$offset) )
			.toggleClass('width-b', ($width <= 1769-$offset) )
			.toggleClass('width-c', ($width <= 1669-$offset) )
			.toggleClass('width-d', ($width <= 1320-$offset) )
			.toggleClass('width-e', ($width <= 1140-$offset) )
			.toggleClass('width-f', ($width <= 1040-$offset) )
			.toggleClass('width-g', ($width <=  980-$offset) )
			.toggleClass('width-h', ($width <=  870-$offset) )
			.toggleClass('width-i', ($width <=  620-$offset) )
			.toggleClass('width-j', ($width <=  450-$offset) );

		$('#box-biomes')
			.toggleClass('width-a', ($width <= 3250-$offset) && ($width >= 2560-$offset) )
			.toggleClass('width-b', ($width <= 1769-$offset) )
			.toggleClass('width-c', ($width <= 1669-$offset) )
			.toggleClass('width-d', ($width <= 1320-$offset) )
			.toggleClass('width-e', ($width <= 1140-$offset) )
			.toggleClass('width-f', ($width <= 1040-$offset) )
			.toggleClass('width-g', ($width <=  980-$offset) )
			.toggleClass('width-h', ($width <=  830-$offset) )
			.toggleClass('width-i', ($width <=  630-$offset) )
			.toggleClass('width-j', ($width <=  428-$offset) );

		$('#box-mechanics')
			.toggleClass('width-a', ($width <= 4500-$offset) && ($width >= 3250-$offset) || $width <= 1470-$offset )
			.toggleClass('width-b', ($width <= 1769-$offset) && ($width >= 1670-$offset) )
			.toggleClass('width-c', ($width <= 1080-$offset) )
			.toggleClass('width-d', ($width <=  750-$offset) )
			.toggleClass('width-e', ($width <=  550-$offset) )
			.toggleClass('width-f', ($width <=  359-$offset) );

		$('#box-npcs')
			.toggleClass('width-a', ($width <= 4500-$offset) && ($width >= 3250-$offset) )
			.toggleClass('width-b', ($width <= 3249-$offset) && ($width >= 2560-$offset) )
			.toggleClass('width-c', ($width <= 1470-$offset) )
			.toggleClass('width-d', ($width <= 1080-$offset) )
			.toggleClass('width-e', ($width <=  720-$offset) )
			.toggleClass('width-f', ($width <=  570-$offset) )
			.toggleClass('width-g', ($width <=  350-$offset) );

		$('#box-bosses')
			.toggleClass('width-a', ($width <= 4500-$offset) && ($width >= 3250-$offset) )
			.toggleClass('width-b', ($width <= 3249-$offset) && ($width >= 2560-$offset) )
			.toggleClass('width-c', ($width <= 1669-$offset) )
			.toggleClass('width-d', ($width <= 1365-$offset) )
			.toggleClass('width-e', ($width <=  800-$offset) )
			.toggleClass('width-f', ($width <=  720-$offset) )
			.toggleClass('width-g', ($width <=  480-$offset) );

		$('#box-events')
			.toggleClass('width-a', ($width <= 4500-$offset) && ($width >= 3250-$offset) )
			.toggleClass('width-b', ($width <= 1669-$offset) )
			.toggleClass('width-c', ($width <= 1365-$offset) )
			.toggleClass('width-d', ($width <=  800-$offset) )
			.toggleClass('width-e', ($width <=  720-$offset) )
			.toggleClass('width-f', ($width <=  650-$offset) )
			.toggleClass('width-g', ($width <=  540-$offset) );

		$('#sect-ext')
			.toggleClass('width-a', $width >= 2300-$offset );

		$('#box-software')
			.toggleClass('width-a', ($width <= 2299-$offset) )
			.toggleClass('width-b', ($width <= 1100-$offset) )
			.toggleClass('width-c', ($width <=  680-$offset) );

		$('#box-wiki')
			.toggleClass('width-a', ($width <= 2299-$offset) )
			.toggleClass('width-b', ($width <= 1499-$offset) )
			.toggleClass('width-c', ($width <=  680-$offset) );
	}

	initiate_collapsible();
	$(window).on('resize', mw.util.debounce( initiate_collapsible, 200) );


	$btn.on('click', function(){
		$header.toggleClass('collapsed');
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/** 
 * Hair Dyes slider, for [[Hair Dyes]] page.
 * Color algorithms are from: {{source code ref|v=1.4.3.6|method=Terraria.Initializers.DyeInitializer.LoadLegacyHairdyes|onlycate=y}}
 */
mw.hook('wikipage.content').add(function() {
	var $sliders = $(".hair-dye-slider-wrapper .slider");
	if (!$sliders.length) {
		return;
	}
	var l10n = l10nFactory(mw.config.get( 'wgPageContentLanguage' ), {
		// time format: prefix + <time> + postfix
		amPrefix:{
			'en': "",
			'zh': "上午&nbsp;",
			'zh-cn': "上午&nbsp;"
		},
		amPostfix:{
			'en': "&nbsp;AM",
			'zh': "",
			'zh-cn': ""
		},
		pmPrefix:{
			'en': "",
			'zh': "下午&nbsp;",
			'zh-cn': "下午&nbsp;"
		},
		pmPostfix:{
			'en': "&nbsp;PM",
			'zh': "",
			'zh-cn': ""
		}
	});
	var pc, gc, sc, cc; // coin templates, filled by loadCoinTemplates()
	var textMoney = function(slidervalue) {
		var money = 2 * Math.pow(slidervalue, 3);
		if (money === 0) {
			return '0&thinsp;' + cc;
		}
		if (slidervalue === 100 || money >= 2000000) {
			money = 2000000;
		}
		var moneyText = (money === 2000000 ? '≥ ' : '');
		var moneyPc = Math.floor(money/1000000);
		money -= moneyPc * 1000000;
		var moneyGc = Math.floor(money/10000);
		money -= moneyGc * 10000;
		var moneySc = Math.floor(money/100);
		money -= moneySc * 100;
		var moneyCc = Math.round(money);
		return moneyText
			+ (moneyPc ? moneyPc + '&thinsp;' + pc : '')
			+ (moneyGc ? moneyGc + '&thinsp;' + gc : '')
			+ (moneySc ? moneySc + '&thinsp;' + sc : '')
			+ (moneyCc ? moneyCc + '&thinsp;' + cc : '');
	};
	var textTime = function(slidervalue) {
		var time = slidervalue*864 + 16200;
		time -= (time > 86400 ? 86400 : 0);
		if (time < 3600) {
			return l10n('amPrefix')
				+ Math.floor(time/3600 + 12) + ":" + Math.round((time/3600 + 12 - Math.floor(time/3600 + 12))*60).toString().padStart(2,0)
				+ l10n('amPostfix');
		} else if (time < 43200) {
			return l10n('amPrefix')
				+ Math.floor(time/3600) + ":" + Math.round((time/3600 - Math.floor(time/3600))*60).toString().padStart(2,0)
				+ l10n('amPostfix');
		} else if (time < 46800) {
			return l10n('pmPrefix')
				+ Math.floor(time/3600) + ":" + Math.round((time/3600 - Math.floor(time/3600))*60).toString().padStart(2,0)
				+ l10n('pmPostfix');
		} else {
			return l10n('pmPrefix')
				+ Math.floor(time/3600 - 12) + ":" + Math.round((time/3600 - 12 - Math.floor(time/3600 - 12))*60).toString().padStart(2,0)
				+ l10n('pmPostfix');
		}
	};
	var colorMoney = function(slidervalue) {
		var num15 = 2 * Math.pow(slidervalue, 3);
		var num16 = 50000;
		var num17 = 500000;
		var num18 = 2000000;
		var color8 = { "R": 226, "G": 118, "B": 76 };
		var color9 = { "R": 174, "G": 194, "B": 196 };
		var color10 = { "R": 204, "G": 181, "B": 72 };
		var color11 = { "R": 161, "G": 172, "B": 173 };
		var newColor = { "R": 255, "G": 255, "B": 255 };
		if (num15 < num16) {
			var num19 = num15 / num16;
			var num20 = 1 - num19;
			newColor.R = color8.R * num20 + color9.R * num19;
			newColor.G = color8.G * num20 + color9.G * num19;
			newColor.B = color8.B * num20 + color9.B * num19;
		}
		else if (num15 < num17) {
			var num22 = (num15 - num16) / (num17 - num16);
			var num23 = 1 - num22;
			newColor.R = color9.R * num23 + color10.R * num22;
			newColor.G = color9.G * num23 + color10.G * num22;
			newColor.B = color9.B * num23 + color10.B * num22;
		}
		else if (num15 < num18) {
			var num25 = (num15 - num17) / (num18 - num17);
			var num26 = 1 - num25;
			newColor.R = color10.R * num26 + color11.R * num25;
			newColor.G = color10.G * num26 + color11.G * num25;
			newColor.B = color10.B * num26 + color11.B * num25;
		}
		else {
			newColor = color11;
		}
		return "rgb(" + newColor.R + "," + newColor.G + "," + newColor.B + ")";
	};
	var colorSpeed = function(slidervalue) {
		var num = slidervalue * 0.1;
		var num2 = 10;
		var num3 = num / num2;
		var num4 = 1 - num3;
		var playerHairColor = { "R": 215, "G": 90, "B": 55 };
		var newColor = { "R": 255, "G": 255, "B": 255 };
		newColor.R = (75 * num3 + playerHairColor.R * num4);
		newColor.G = (255 * num3 + playerHairColor.G * num4);
		newColor.B = (200 * num3 + playerHairColor.B * num4);
		return "rgb(" + newColor.R + "," + newColor.G + "," + newColor.B + ")";
	};
	var colorTime = function(slidervalue) {
		var time = slidervalue*864 + 16200;
		time -= (time > 86400 ? 86400 : 0);
		var color4 = { "R": 1, "G": 142, "B": 255 };
		var color5 = { "R": 255, "G": 255, "B": 0 };
		var color6 = { "R": 211, "G": 45, "B": 127 };
		var color7 = { "R": 67, "G": 44, "B": 118 };
		var newColor = { "R": 255, "G": 255, "B": 255 };
		if (time >= 16200 && time < 70200) {
			if (time < 43200) {
				var num5 = time / 43200;
				var num6 = 1 - num5;
				newColor.R = (color4.R * num6 + color5.R * num5);
				newColor.G = (color4.G * num6 + color5.G * num5);
				newColor.B = (color4.B * num6 + color5.B * num5);
			} else {
				var num7 = 43200;
				var num8 = ((time - num7) / (70200 - num7));
				var num9 = 1 - num8;
				newColor.R = (color5.R * num9 + color6.R * num8);
				newColor.G = (color5.G * num9 + color6.G * num8);
				newColor.B = (color5.B * num9 + color6.B * num8);
			}
		} else {
			if (time >= 70200 && time < 86400) {
				var num10 = (time / 86400);
				var num11 = 1 - num10;
				newColor.R = (color6.R * num11 + color7.R * num10);
				newColor.G = (color6.G * num11 + color7.G * num10);
				newColor.B = (color6.B * num11 + color7.B * num10);
			} else {
				var num12 = 0;
				var num13 = ((time - num12) / (16200 - num12));
				var num14 = 1 - num13;
				newColor.R = (color7.R * num14 + color4.R * num13);
				newColor.G = (color7.G * num14 + color4.G * num13);
				newColor.B = (color7.B * num14 + color4.B * num13);
			}
		}
		return "rgb(" + newColor.R + "," + newColor.G + "," + newColor.B + ")";
	};
	var colorFunc = function ($type, $value) {
		switch($type) {
			case "health":
				return "rgb(" + ($value * 2.35 + 20) + ",20,20)";
			case "mana":
				return "rgb(" + (250 - $value * 2) + "," + (255 - $value * 1.80) + ",255)";
			case "money":
				return colorMoney($value);
			case "speed":
				return colorSpeed($value);
			case "time":
				return colorTime($value);
			default:
				return "#0ff";
		}
	};
	var textFunc = function ($type, $value) {
		// return the function from the textFunctions table if the id is correct
		// otherwise, return a fallback function that just returns the raw, unchanged slider value
		switch($type) {
			case "money":
				return textMoney($value);
			case "speed":
				return (($value === 100) ? "≥ 51" : Math.round($value/10 * 3.75*(15/11)));
			case "time":
				return textTime($value);
			default:
				return $value;
		}
	};
	var update = function($slider) {
		var $value = parseInt($slider.data('input').val());
		var $type = $slider.data('type');
		// update color display
		$slider.data('colorBox').css('background-color', colorFunc($type, $value));
		// update text display
		$slider.data('valueBox').html(textFunc($type, $value));
	};
	var loadCoinTemplates = function() {
		return new mw.Api().get({
			action: 'parse',
			prop: 'text',
			title: mw.config.get('wgPageName'),
			text: '{{pc}}__.__{{gc}}__.__{{sc}}__.__{{cc}}',
			disablelimitreport: true,
			wrapoutputclass: '' // disable the <div> wrapper
		}).then(function(apiResult) {
			html = apiResult.parse.text['*'];
			// strip the surrounding "<p>...\n</p>"
			html = html.substring('<p>'.length, html.length - '\n</p>'.length);
			var templateOutputs = html.split('__.__');
			pc = templateOutputs[0];
			gc = templateOutputs[1];
			sc = templateOutputs[2];
			cc = templateOutputs[3];
		});
	};
	// prepare the coin templates; then create all sliders and make them visible
	loadCoinTemplates().then(function() {
		$sliders.each(function() {
			var $slider = $(this).append($("<input type='range' style='margin: auto 0.5em'/>"));
			var $wrapper = $slider.parents('.hair-dye-slider-wrapper').show();
			var $valueBox = $wrapper.find(".inputvalue");
			var $input = $slider.find('input').val($valueBox.text()).on('input', function() {
				update($slider);
			});
			$slider.val($valueBox.text()).data({
				valueBox: $valueBox,
				colorBox: $wrapper.find(".color-box"),
				input: $input,
				type: $wrapper.attr('id')
			});
			update($slider);
		});
	});
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////

/**
 * TEST: portlet link for 'Legacy:' pages
 */
mw.hook('wikipage.content').add(function() {
	var linktarget = $('#test-marker-for-new-portlet-link').data('linktarget');
	if (linktarget !== undefined) {
		var newId, insertBefore, text, hovertext;
		switch (mw.config.get('wgNamespaceNumber')) {
			case 0:
				newId = 'ca-nstab-legacy';
				insertBefore = '#ca-talk';
				text = 'Legacy';
				hovertext = 'Differences on legacy versions';
				break;
			case 11000:
				newId = 'ca-nstab-main';
				insertBefore = '#ca-nstab-legacy';
				text = 'Page';
				hovertext = 'Main content (modern versions)';
				break;
			default:
				return;
		}
		mw.util.addPortletLink('p-namespaces', linktarget, text, newId, hovertext, null, insertBefore);
	}
});

///////////////////////////////////////////////////////////////////////////////////////////////////////////////