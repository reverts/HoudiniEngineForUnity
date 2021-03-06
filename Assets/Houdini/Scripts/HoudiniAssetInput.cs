﻿/*
 * PROPRIETARY INFORMATION.  This software is proprietary to
 * Side Effects Software Inc., and is not to be reproduced,
 * transmitted, or disclosed in any way without written permission.
 *
 * Produced by:
 *      Side Effects Software Inc
 *		123 Front Street West, Suite 1401
 *		Toronto, Ontario
 *		Canada   M5J 2M2
 *		416-504-9876
 *
 * COMMENTS:
 * 
 */

using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Utility = HAPI_AssetUtility;

[ ExecuteInEditMode ]
[ RequireComponent( typeof( MeshFilter ) ) ]
public class HAPI_AssetInput : HAPI_Asset
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Public Properties

	// Please keep these in the same order and grouping as their initializations in HAPI_Asset.reset().

	public bool			prShowAttributesTable {			get { return myShowAttributesTable; }
														set { myShowAttributesTable = value; } }
	public bool			prHasAttributeChanges {			get { return myHasAttributeChanges; }
														set { myHasAttributeChanges = value; } }

	public bool			prHasError {					get { return myErrorMsg != ""; }
														private set {} }
	public string		prErrorMsg {					get { return myErrorMsg; }
														set { myErrorMsg = value; } }

	public Mesh			prEditableMesh {				get { return myEditableMesh; }
														set { myEditableMesh = value; } }
	public Mesh			prOriginalMesh {				get { return myOriginalMesh; }
														set { myOriginalMesh = value; } }
	public HAPI_GeoAttributeManager prGeoAttributeManager { get { return myGeoAttributeManager; }
															private set {} }

	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Public Methods
	
	public HAPI_AssetInput() 
	{
		if ( prEnableLogging )
			Debug.Log( "HAPI_Asset created!" );

		reset();
	}
	
	~HAPI_AssetInput()
	{}

	public override void reset()
	{
		base.reset();

		myShowAttributesTable = true;
		myHasAttributeChanges = false;

		myErrorMsg = "";

		prEditableMesh = null;
		prOriginalMesh = null;
		
		// Overwrite some settings that should be different by default for input assets than other asset types.
		prAutoSelectAssetRootNode	= false;
		prHideGeometryOnLinking		= false;
		prAssetType					= AssetType.TYPE_INPUT;

		myGeoAttributeManager		= null;
	}

	public override void OnEnable()
	{
		base.OnEnable();

		// We want to preserve the transform of the original mesh as we
		// assetize it.
		myLastLocalToWorld = transform.localToWorldMatrix;

		if ( prAssetId < 0 )
			buildAll();
	}
	
	public override bool build( bool reload_asset, bool unload_asset_first,
								bool serialization_recovery_only,
								bool force_reconnect,
								bool cook_downstream_assets,
								bool use_delay_for_progress_bar ) 
	{
		if ( !validateAttributes() )
			return false;

		unload_asset_first = unload_asset_first && ( !serialization_recovery_only || isPrefab() );

		bool base_built = base.build( reload_asset, unload_asset_first, serialization_recovery_only, 
									  force_reconnect, cook_downstream_assets, use_delay_for_progress_bar );
		if ( !base_built )
			return false;

		myHasAttributeChanges = false;

		return true;
	}

	public void resetFull()
	{
		// Safe to assume these exist because of [RequiredComponent] attributes.
		MeshFilter mesh_filter = gameObject.GetComponent< MeshFilter >();
		if ( prOriginalMesh )
			mesh_filter.sharedMesh = prOriginalMesh;

		HAPI_Host.destroyAsset( prAssetId );

		reset();
	}

	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Protected Methods

	protected override int buildCreateAsset()
	{
		return HAPI_Host.createInputAsset( transform.name );
	}

	protected override void buildFullBuildCustomWork( ref HAPI_ProgressBar progress_bar )
	{
		cloneMesh();

		if ( myGeoAttributeManager == null )
		{
			MeshRenderer mesh_renderer = getOrCreateComponent< MeshRenderer >();
			MeshCollider mesh_collider = getOrCreateComponent< MeshCollider >();

			myGeoAttributeManager = ScriptableObject.CreateInstance< HAPI_GeoAttributeManager >();
			myGeoAttributeManager.init( prEditableMesh, mesh_renderer, mesh_collider, transform );
		}
	}

	protected override void buildCreateObjects( bool reload_asset, ref HAPI_ProgressBar progress_bar )
	{
		try
		{
			const int object_id = 0;
			const int geo_id = 0;

			// Write marshalled geo to Input Asset.
			HAPI_AssetUtility.setMesh(
				prAssetId, object_id, geo_id, ref myEditableMesh, null, myGeoAttributeManager );

			// Apply the input asset transform to the marshaled object in the Houdini scene.
			HAPI_TransformEuler trans = Utility.getHapiTransform( transform.localToWorldMatrix );
			HAPI_Host.setObjectTransform( prAssetId, object_id, trans );

			// Marshall in the animation.
			Animation anim_component = GetComponent< Animation >();
			if ( anim_component )
				if ( anim_component.clip != null )
					marshalCurvesFromClip( prObjectNodeId, anim_component.clip );
				else
					foreach ( AnimationState anim_state in anim_component )
					{
						AnimationClip clip = anim_component.GetClip( anim_state.name );
						if ( clip != null )
						{
							marshalCurvesFromClip( prObjectNodeId, clip );
							break;
						}
					}

			HAPI_Host.repaint();
		}
		catch ( HAPI_Error )
		{
			// Per-object errors are not re-thrown so that the rest of the asset has a chance to load.
			//Debug.LogWarning( error.ToString() );
		}
	}

	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Private

	private bool validateAttributes()
	{
		if ( !myGeoAttributeManager )
			return true;

		// Check for duplicates.
		for ( int i = 0; i < myGeoAttributeManager.prAttributes.Count; ++i )
			for ( int j = i + 1; j < myGeoAttributeManager.prAttributes.Count; ++j )
				if ( myGeoAttributeManager.prAttributes[ i ].prName ==
					myGeoAttributeManager.prAttributes[ j ].prName )
				{
					myErrorMsg = "Duplicate attribute name: " + myGeoAttributeManager.prAttributes[ i ].prName;
					return false;
				}

		// Check for invalid attribute names.
		Regex attribute_name_regex = new Regex( "^[a-zA-Z0-9-_]*$" );
		foreach ( HAPI_GeoAttribute attribute in myGeoAttributeManager.prAttributes )
		{
			if ( attribute.prName == "" )
			{
				myErrorMsg = "You have an empty attribute name.";
				return false;
			}

			if ( !attribute_name_regex.IsMatch( attribute.prName ) )
			{
				myErrorMsg = "Attribute names cannot contain special characters: " + attribute.prName;
				return false;
			}

			int temp;
			if ( int.TryParse( attribute.prName.Substring( 0, 1 ), out temp ) )
			{
				myErrorMsg = "Attribute cannot start with a number: " + attribute.prName;
				return false;
			}

			if ( attribute.prName == "P" )
			{
				myErrorMsg = "Cannot have an attribute named 'P' as that attribute is reserved for positions.";
				return false;
			}
		}

		myErrorMsg = "";

		return true;
	}

	private void cloneMesh()
	{
		// Safe to assume these exist because of [RequiredComponent] attributes.
		MeshFilter mesh_filter = gameObject.GetComponent< MeshFilter >();

		// Create the editable mesh from the original mesh. We don't want to
		// modify the original mesh because it is likely shared by many
		// instances.
		if ( !prEditableMesh )
		{
			prOriginalMesh = mesh_filter.sharedMesh;
			if ( !prOriginalMesh )
				prOriginalMesh = mesh_filter.mesh;
			if ( !prOriginalMesh )
				throw new HAPI_ErrorNotFound( "No mesh found on the Mesh Filter!" );

			prEditableMesh = Mesh.Instantiate( prOriginalMesh ) as Mesh;
			prEditableMesh.name = prOriginalMesh.name + " (Editable Copy)";

			Color[] colours = new Color[ prEditableMesh.vertexCount ];
			for ( int i = 0; i < prEditableMesh.vertexCount; ++i )
				colours[ i ] = new Color( 1.0f, 1.0f, 1.0f );
			prEditableMesh.colors = colours;

			mesh_filter.sharedMesh = prEditableMesh;
		}
	}

	[SerializeField] private bool			myShowAttributesTable;
	[SerializeField] private bool			myHasAttributeChanges;

	[SerializeField] private string			myErrorMsg;

	[SerializeField] private Mesh			myEditableMesh;
	[SerializeField] private Mesh			myOriginalMesh;

	[SerializeField] private HAPI_GeoAttributeManager myGeoAttributeManager;
}
