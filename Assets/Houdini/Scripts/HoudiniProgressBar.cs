using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;

public class HAPI_ProgressBar
{
	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Public Properties

	public System.DateTime	prStartTime { get; set; }
	public System.TimeSpan	prCurrentDuration { get; set; }
	public int				prCurrentValue { get; set; }
	public int				prTotal { get; set; }
	public string			prTitle { get; set; }
	public string			prMessage { get; set; }
	public bool				prUseDelay { get; set; }
	public HAPI_Asset		prAsset { get; set; }

	/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
	// Public Methods
	
	public HAPI_ProgressBar() 
	{
		prCurrentValue		= 0;
		prCurrentDuration	= new System.TimeSpan( 0 );
		prTotal				= 0;
		prTitle				= "Building Houdini Asset";
		prMessage			= "Doing stuff.";
		prStartTime			= System.DateTime.Now;

#if UNITY_EDITOR
		myLastValue			= -1;
		myLastMsg			= "";
#endif // UNITY_EDITOR

		prUseDelay			= true;

		prAsset				= null;
	}

	~HAPI_ProgressBar()
	{
		// Cannot clear progress bar here because clearProgressBar can only be called
		// from the main thread while destructors are usually called by another thread
		// (the garbage collector).
		// clearProgressBar();
	}
	
	public void statusCheckLoop()
	{
#if UNITY_EDITOR
		HAPI_State state = HAPI_State.HAPI_STATE_STARTING_LOAD;
		prCurrentValue = 0;
		prTotal = 100;

		bool progress_cancelled = false;

		while ( state != HAPI_State.HAPI_STATE_READY && state != HAPI_State.HAPI_STATE_READY_WITH_ERRORS )
		{
			state = (HAPI_State) HAPI_Host.getStatus( HAPI_StatusType.HAPI_STATUS_STATE );

			if ( state == HAPI_State.HAPI_STATE_COOKING )
			{
				prCurrentValue = HAPI_Host.getCookingCurrentCount();
				prTotal = HAPI_Host.getCookingTotalCount();
			}
			else
			{
				prCurrentValue = ( System.DateTime.Now - prStartTime ).Seconds;
				prTotal = 100;
			}

			prMessage = HAPI_Host.getStatusString( HAPI_StatusType.HAPI_STATUS_STATE );

			if ( progress_cancelled )
				EditorUtility.DisplayProgressBar( prTitle, "Aborting...", 0 );
			else
			{
				try
				{
					displayProgressBar();
				}
				catch ( HAPI_ErrorProgressCancelled )
				{
					progress_cancelled = true;
					EditorUtility.DisplayProgressBar( prTitle, "Aborting...", 0 );
				}
			}
		}

		// We want to propage the cancellation of the progress still, even if it is after a delay.
		if ( progress_cancelled )
			throw new HAPI_ErrorProgressCancelled();

		if ( state == HAPI_State.HAPI_STATE_READY_WITH_ERRORS )
		{
			state = HAPI_State.HAPI_STATE_READY;
			HAPI_Host.throwRuntimeError();
		}
#endif // UNITY_EDITOR
	}
	
	public void displayProgressBar()
	{
		displayProgressBar( prCurrentValue );
	}

	public void incrementProgressBar()
	{
		incrementProgressBar( 1 );
	}

	public void incrementProgressBar( int increment )
	{
		prCurrentValue += increment;
		displayProgressBar( prCurrentValue );
	}

	public void clearProgressBar()
	{		
		prCurrentValue = 0;
#if UNITY_EDITOR
		EditorUtility.ClearProgressBar();
#endif // UNITY_EDITOR
	}

	protected void displayProgressBar( int value )
	{
#if UNITY_EDITOR
		System.DateTime current = System.DateTime.Now;
		System.TimeSpan delta = current - prStartTime;
		
		// This delay for displaying the progress bar is so the bar won't flicker for really quick updates
		// (less than a few seconds). Also, when we do show the progress bar the focus of the current 
		// inspector control is lost.
		if ( prUseDelay && delta.TotalSeconds < HAPI_Constants.HAPI_SEC_BEFORE_PROGRESS_BAR_SHOW )
		{
			EditorUtility.ClearProgressBar();
			return;
		}
		
		// If there are no changes to the progress bar value or message don't re-display it again.
		if ( value == myLastValue && prMessage == myLastMsg
			 && delta == prCurrentDuration )
			return;
		
		prCurrentValue = value;
		string message = "";
		if ( delta.Hours > 0 )
			message = delta.Hours + "h " + delta.Minutes + "m " + delta.Seconds + "s - " + prMessage;
		else if ( delta.Minutes > 0 )
			message = delta.Minutes + "m " + delta.Seconds + "s - " + prMessage;
		else if ( delta.Seconds > 0 )
			message = delta.Seconds + "s - " + prMessage;
		else
			message = prMessage;

		string title = prTitle;
		if ( prAsset != null && prAsset.prAssetName != "ASSET_NAME" )
		{
			if ( prAsset.isPrefab() )
				title = "Building Houdini Asset Prefab: " + prAsset.prAssetName;
			else
				title = "Building Houdini Asset: " + prAsset.prAssetName;
		}

		bool result = !EditorUtility.DisplayCancelableProgressBar( 
								title, message, Mathf.InverseLerp( 0, prTotal, prCurrentValue ) );
		
		if ( !result )
		{
			prCurrentDuration = new System.TimeSpan( 0 );
			myLastValue = -1;
			myLastMsg = "";
			HAPI_Host.interrupt();
			throw new HAPI_ErrorProgressCancelled();
		}
		else
		{
			myLastValue = value;
			myLastMsg = prMessage;
			prCurrentDuration = delta;
		}
#endif // UNITY_EDITOR
	}
	
#if UNITY_EDITOR
	// Used to reduce the update frequency of the progress bar so it doesn't flicker.
	private int					myLastValue;
	private string				myLastMsg;
#endif // UNITY_EDITOR
}
